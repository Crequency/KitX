using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX.KXP.Tool
{
    internal static class Copyrighter
    {
        internal static void WriteCopyrightInfo()
        {
            Console.WriteLine(
                $"" +
                $"KitX Tools | KitX.KXP.Tool\n" +
                $"Copyright (C) Crequency Studio 2022-present.\n" +
                $"All Rights Reserved." +
                $""
            );
        }
    }
}
