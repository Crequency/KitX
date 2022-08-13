using System.Drawing;

namespace KitX_Installer_for_Windows_in.NET_Framework
{
    internal static class Drawer
    {
        internal static void DrawBlock(Graphics graph, Pen p, int x, int y, int size)
        {
            graph.DrawRectangle(p, new Rectangle()
            {
                X = x,
                Y = y,
                Width = size,
                Height = size
            });
        }

        internal static void DrawBlockWithShadow(Graphics graph, int x, int y, int size,
            int offset, int shadowSize, int breakNess)
        {
            Pen p1 = new Pen(Color.FromArgb(0, 0, 0)),
                p2 = new Pen(Color.FromArgb(150, 150, 150)),
                p3 = new Pen(Color.FromArgb(90, 90, 90)),
                p4 = new Pen(Color.FromArgb(130, 130, 130));
            Rectangle r1 = new Rectangle(x, y, size - 2, size - 2),
                r2 = new Rectangle(r1.Right, y + offset, shadowSize, size - offset),
                r3 = new Rectangle(x + offset, r1.Bottom, size - offset, shadowSize),
                r4 = new Rectangle(r1.Right, r1.Bottom, shadowSize - breakNess, shadowSize - breakNess);
            graph.DrawRectangle(p1, r1);
            graph.DrawRectangle(p2, r2);
            graph.DrawRectangle(p3, r3);
            graph.DrawRectangle(p4, r4);
        }

        internal static void PixelBackground(Graphics g)
        {
            int[][] DrawIt = new int[10][]{
                new int[24]{1,1,1,0,0,0,1,1,1,0,0,0,0,0,1,1,1,1,1,1,0,1,1,1},
                new int[24]{0,1,1,0,0,1,1,1,0,0,0,0,0,0,1,1,1,1,1,0,0,1,1,0},
                new int[24]{0,1,1,0,1,1,1,0,0,0,0,0,0,0,0,1,1,1,0,0,0,1,0,0},
                new int[24]{0,1,1,1,1,1,0,0,0,1,0,0,1,0,0,0,1,1,1,0,1,0,0,0},
                new int[24]{0,1,1,1,1,0,0,0,0,0,0,0,1,0,0,0,0,1,1,1,0,0,0,0},
                new int[24]{0,1,1,1,1,1,0,0,0,1,0,1,1,1,0,0,0,0,1,1,1,0,0,0},
                new int[24]{0,1,1,1,1,1,1,0,1,1,0,0,1,0,0,0,0,1,0,1,1,1,0,0},
                new int[24]{0,1,1,0,1,1,1,1,0,1,0,0,1,0,0,0,1,0,0,0,1,1,1,0},
                new int[24]{0,1,1,0,0,1,1,1,1,1,0,1,1,0,0,1,1,0,0,1,1,1,1,1},
                new int[24]{1,1,1,1,0,0,1,1,1,1,1,0,1,1,1,1,1,0,1,1,1,1,1,1}
            };

            for (int i = 0; i < 10; ++i)
                for (int j = 0; j < 24; ++j)
                    if (DrawIt[i][j] == 1)
                        DrawStantardPixelBlockWithIndexConverter(g, j, i + 2);
        }

        internal static void DrawStantardPixelBlockWithIndexConverter(Graphics g, int x, int y) =>
            DrawStantardPixelBlock(g, x * 30 + 30, y * 30);

        internal static void DrawStantardPixelBlock(Graphics g, int x, int y) =>
            DrawBlockWithShadow(g, x, y, 30, 2, 2, 1);
    }
}
