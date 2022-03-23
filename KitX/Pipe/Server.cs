using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BasicHelper.IO.PipeHelper;

#pragma warning disable IDE0044 // 添加只读修饰符

namespace KitX.Pipe
{
    public class Server
    {
        private Dictionary<string, Thread> servers = new();

        public Server()
        {

        }

        public void AddThread(Core.IContract contract)
        {
            string pgid = contract.GetName().GetHashCode().ToString();
            var tr = new Thread(() =>
            {
                NamedPipeServerStream pipeServer = new(pgid, PipeDirection.InOut);
                pipeServer.WaitForConnection();
                StreamString ss = new(pipeServer);
            });
            servers.Add(pgid, tr);
            tr.Start();
        }
    }
}

#pragma warning restore IDE0044 // 添加只读修饰符