using System;
using System.IO;
using System.Threading;

while (true)
{
    if (Directory.Exists("Config"))
        Directory.Delete("Config", true);

    Thread.Sleep(5000);
}
