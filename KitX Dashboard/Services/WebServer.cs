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

namespace KitX_Dashboard.Services
{
    public class WebServer
    {
        public readonly NamedPipeServerStream pipeServer;

        public WebServer(string name, PipeDirection direction, int maxThreads)
        {
            pipeServer = new(name, direction, maxThreads);

            new Thread(() =>
            {
                int threadId = Environment.CurrentManagedThreadId;

                pipeServer.WaitForConnection();
                try
                {
                    StreamString ss = new(pipeServer);

                    ss.WriteString("I am the one true server!");
                    string filename = ss.ReadString();

                    ReadFileToStream fileReader = new(ss, filename);

                    Console.WriteLine("Reading file: {0} on thread[{1}] as user: {2}.",
                        filename, threadId, pipeServer.GetImpersonationUserName());
                    pipeServer.RunAsClient(fileReader.Start);
                }
                catch (IOException e)
                {
                    Program.LocalLogger.Log("Logger_Debug", e.Message, LoggerManager.LogLevel.Error);
                }
                pipeServer.Close();
            }).Start();
        }
    }

    public class StreamString
    {
        private readonly Stream ioStream;
        private readonly UnicodeEncoding streamEncoding;

        public StreamString(Stream ioStream)
        {
            this.ioStream = ioStream;
            streamEncoding = new UnicodeEncoding();
        }

        public string ReadString()
        {
            int len = ioStream.ReadByte() * 256;
            len += ioStream.ReadByte();
            byte[] inBuffer = new byte[len];
            ioStream.Read(inBuffer, 0, len);

            return streamEncoding.GetString(inBuffer);
        }

        public int WriteString(string outString)
        {
            byte[] outBuffer = streamEncoding.GetBytes(outString);
            int len = outBuffer.Length;
            if (len > UInt16.MaxValue)
            {
                len = (int)UInt16.MaxValue;
            }
            ioStream.WriteByte((byte)(len / 256));
            ioStream.WriteByte((byte)(len & 255));
            ioStream.Write(outBuffer, 0, len);
            ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }

    public class ReadFileToStream
    {
        private readonly string fn;
        private readonly StreamString ss;

        public ReadFileToStream(StreamString str, string filename)
        {
            fn = filename;
            ss = str;
        }

        public void Start()
        {
            string contents = File.ReadAllText(fn);
            ss.WriteString(contents);
        }
    }
}
