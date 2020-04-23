using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LuaInterface;
using System;

public class TestLuaProtobuf : MonoBehaviour {
    //lua-protobuf 测试用lua脚本

    //proto2

    // assert(protoc:load[[
    //            message Phone {
    //                 optional string name = 1;
    //                 optional int64  phonenumber = 2;
    //             }
    //             message Person
    //             {
    //                 optional string name = 1;
    //             optional int32  age      = 2;
    //               optional string address = 3;
    //             repeated Phone  contacts = 4;
    //            } ]])

    // proto3

    // assert(protoc:load [[
    //             syntax = 'proto3';
    //             message Phone {
    //                 string name = 1;
    //                 int64 phonenumber = 2;
    //             }
    //             message Person {
    //                 string name = 1;
    //                 int32 age = 2;
    //                 string address = 3;
    //                 repeated Phone contacts = 4;
    //             } ]])
    string script =
        @"
            local pb = require 'pb'
            local protoc = require 'lua-protobuf.protoc'

            -- load schema from text

           assert(protoc:load [[
                syntax = 'proto3';
                
                message Phone {
                    string name = 1;
                    int64 phonenumber = 2;
                }
                message Person {
                    string name = 1;
                    int32 age = 2;
                    string address = 3;
                    repeated Phone contacts = 4;
                } ]])


            -- lua table data
            local data = {
               name = 'ilse',
               age  = 18,
               contacts = {
                  { name = 'alice', phonenumber = 12312341234 },
                  { name = 'bob',   phonenumber = 45645674567 }
               }
            }

            -- encode lua table data into binary format in lua string and return
            local bytes = assert(pb.encode('Person', data))
            print(pb.tohex(bytes))

            -- and decode the binary data back into lua table
            local data2 = assert(pb.decode('Person', bytes))
            print(require 'lua-protobuf.serpent'.block(data2))
        ";

    int bundleCount = int.MaxValue;
    string tips = null;
    LuaState luaState = null;

    bool testAB = false;//是否打包AB加载

    void Start()
    {
#if UNITY_5 || UNITY_2017 || UNITY_2018
        Application.logMessageReceived += ShowTips;
#else
        Application.RegisterLogCallback(ShowTips);
#endif
        if (!testAB) 
        {
            OnBundleLoad();
            return;
        }
        LuaFileUtils file = new LuaFileUtils();
        file.beZip = true;
#if UNITY_ANDROID && UNITY_EDITOR
        if (IntPtr.Size == 8)
        {
            throw new Exception("can't run this in unity5.x process for 64 bits, switch to pc platform, or run it in android mobile");
        }
#endif
        StartCoroutine(LoadBundles());
    }

    void OnBundleLoad()
    {
        luaState = new LuaState();
        luaState.Start();
        OpenLuaProtobuf();
        luaState.DoString(script);
        luaState.Dispose();
        luaState = null;

    }
    /// <summary>
    /// 这里tolua在OpenLibs的时候并不一定会注册，这里注册一下
    /// </summary>
    void OpenLuaProtobuf1()
    {
        // 这种方法不行
        luaState.LuaGetField(LuaIndexes.LUA_REGISTRYINDEX, "_LOADED");
        luaState.OpenLibs(LuaDLL.luaopen_pb);
        luaState.LuaSetField(-2, "pb");

        luaState.OpenLibs(LuaDLL.luaopen_pb_io);
        luaState.LuaSetField(-2, "pb.io");

        luaState.OpenLibs(LuaDLL.luaopen_pb_conv);
        luaState.LuaSetField(-2, "pb.conv");

        luaState.OpenLibs(LuaDLL.luaopen_pb_buffer);
        luaState.LuaSetField(-2, "pb.buffer");

        luaState.OpenLibs(LuaDLL.luaopen_pb_slice);
        luaState.LuaSetField(-2, "pb.slice");
    }

    // ----------------------------- new code ------------------------------------

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int LuaOpen_PB(IntPtr L)
    {
        return LuaDLL.luaopen_pb(L);
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int LuaOpen_PB_IO(IntPtr L)
    {
        return LuaDLL.luaopen_pb_io(L);
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int LuaOpen_PB_CONV(IntPtr L)
    {
        return LuaDLL.luaopen_pb_conv(L);
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int LuaOpen_PB_BUFFER(IntPtr L)
    {
        return LuaDLL.luaopen_pb_buffer(L);
    }

    [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
    static int LuaOpen_PB_SLICE(IntPtr L)
    {
        return LuaDLL.luaopen_pb_slice(L);
    }

    void OpenLuaProtobuf()
    {

        luaState.BeginPreLoad();
        luaState.RegFunction("pb", LuaOpen_PB);
        luaState.RegFunction("pb.io", LuaOpen_PB_IO);
        luaState.RegFunction("pb.conv", LuaOpen_PB_CONV);
        luaState.RegFunction("pb.buffer", LuaOpen_PB_BUFFER);
        luaState.RegFunction("pb.slice", LuaOpen_PB_SLICE);
        luaState.EndPreLoad(); 
    }

    // ----------------------------- new code ------------------------------------

    void ShowTips(string msg, string stackTrace, LogType type)
    {
        tips += msg;
        tips += "\r\n";
    }

    void OnGUI()
    {
        GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 150, 400, 300), tips);
    }

    void OnApplicationQuit()
    {
#if UNITY_5 || UNITY_2017 || UNITY_2018
        Application.logMessageReceived -= ShowTips;
#else
        Application.RegisterLogCallback(null);
#endif
    }

    IEnumerator CoLoadBundle(string name, string path)
    {
        using (WWW www = new WWW(path))
        {
            if (www == null)
            {
                Debugger.LogError(name + " bundle not exists");
                yield break;
            }

            yield return www;

            if (www.error != null)
            {
                Debugger.LogError(string.Format("Read {0} failed: {1}", path, www.error));
                yield break;
            }

            --bundleCount;
            LuaFileUtils.Instance.AddSearchBundle(name, www.assetBundle);
            www.Dispose();
        }
    }

    IEnumerator LoadFinished()
    {
        while (bundleCount > 0)
        {
            yield return null;
        }

        OnBundleLoad();
    }

    public IEnumerator LoadBundles()
    {
        string streamingPath = Application.streamingAssetsPath.Replace('\\', '/');

#if UNITY_5 || UNITY_2017 || UNITY_2018
#if UNITY_ANDROID && !UNITY_EDITOR
        string main = streamingPath + "/" + LuaConst.osDir + "/" + LuaConst.osDir;
#else
        string main = "file:///" + streamingPath + "/" + LuaConst.osDir + "/" + LuaConst.osDir;
#endif
        WWW www = new WWW(main);
        yield return www;

        AssetBundleManifest manifest = (AssetBundleManifest)www.assetBundle.LoadAsset("AssetBundleManifest");
        List<string> list = new List<string>(manifest.GetAllAssetBundles());
#else
        //此处应该配表获取
        List<string> list = new List<string>() { "lua.unity3d", "lua_cjson.unity3d", "lua_system.unity3d", "lua_unityengine.unity3d", "lua_protobuf.unity3d", "lua_misc.unity3d", "lua_socket.unity3d", "lua_system_reflection.unity3d" };
#endif
        bundleCount = list.Count;

        for (int i = 0; i < list.Count; i++)
        {
            string str = list[i];

#if UNITY_ANDROID && !UNITY_EDITOR
            string path = streamingPath + "/" + LuaConst.osDir + "/" + str;
#else
            string path = "file:///" + streamingPath + "/" + LuaConst.osDir + "/" + str;
#endif
            string name = Path.GetFileNameWithoutExtension(str);
            StartCoroutine(CoLoadBundle(name, path));
        }

        yield return StartCoroutine(LoadFinished());
    }
}

