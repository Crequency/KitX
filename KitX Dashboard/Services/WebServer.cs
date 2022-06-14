using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO.Pipes;
using System.Threading;
using System.IO;
using BasicHelper.LiteLogger;
using KitX_Dashboard.Data;
using BasicHelper.IO.PipeHelper;
using BasicHelper.Util;

#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard.Services
{
    public class WebServer
    {
        public readonly NamedPipeServerStream pipeServer = new("KitX PipeServer",
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                pipeServer.BeginWaitForConnection((o) =>
                {
                    NamedPipeServerStream pServer = (NamedPipeServerStream)o.AsyncState;
                    pServer.EndWaitForConnection(o);
                    StreamString ss = new(pServer);
                    while (GlobalInfo.PipeServerRunning)
                    {

                        //throw new Result<string>($"连接成功, 消息是{ss.ReadString()}");
                    }
                    ss.Dispose();
                }, pipeServer);
            });
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
