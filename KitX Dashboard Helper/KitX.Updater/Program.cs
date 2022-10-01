using Common.Update.Replacer;

namespace KitX.Updater
{
    public class Updater
    {
        /// <summary>
        /// 换个颜色执行
        /// </summary>
        /// <param name="cc">新颜色</param>
        /// <param name="action">动作</param>
        private static void DoColor(ConsoleColor cc, Action action)
        {
            ConsoleColor now = Console.ForegroundColor;
            Console.ForegroundColor = cc;
            action();
            Console.ForegroundColor = now;
        }

        public static void Main(string[] args)
        {
            try
            {
                string _rootDir = string.Empty;
                string _newFilesDir = string.Empty;

                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--source-dir":
                            if (i != args.Length - 1)
                            {
                                ++i;
                                _rootDir = args[i];
                            }
                            else throw new Exception("参数 --source-dir 缺少值");
                            break;
                        case "--update-from":
                            if (i != args.Length - 1)
                            {
                                ++i;
                                _newFilesDir = args[i];
                            }
                            else throw new Exception("参数 --update-from 缺少值");
                            break;
                    }
                }

                Replacer replacer = new Replacer()
                    .SetSourceDir(_newFilesDir)
                    .SetRootDir(_rootDir);
                replacer.Replace();
            }
            catch (Exception e)
            {
                DoColor(ConsoleColor.Red, new(() =>
                {
                    Console.WriteLine(e.Message);
                }));
            }
        }
    }
}

