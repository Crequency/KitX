// KitX Installer for Windows.cpp : 定义应用程序的入口点。
//

#include "framework.h"
#include "KitX Installer for Windows in Win32.h"

#pragma comment(linker,"/manifestdependency:\"type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")

#define MAX_LOADSTRING 100

#define ScreenX GetSystemMetrics(SM_CXSCREEN)
#define ScreenY GetSystemMetrics(SM_CYSCREEN)

#define ID_EDIT_PATH  101

// 全局变量:
HINSTANCE hInst;                                // 当前实例
WCHAR szTitle[MAX_LOADSTRING];                  // 标题栏文本
WCHAR szWindowClass[MAX_LOADSTRING];            // 主窗口类名

// 此代码模块中包含的函数的前向声明:
ATOM                MyRegisterClass(HINSTANCE hInstance);
BOOL                InitInstance(HINSTANCE, int);
LRESULT CALLBACK    WndProc(HWND, UINT, WPARAM, LPARAM);

void DrawBackground(HDC* hdc);
void DrawRectangle(HDC* hdc, int x, int y, int width, int height, int thinkness);
void OnEraseBkGnd(HWND hwnd);
void PixelBackground(HDC* hdc);
void DrawBlock(HDC* hdc, int x, int y, int size);
void DrawBlockWithShadow(HDC* hdc, int x, int y, int size, int offset, int shadowSize, int breakness);
void DrawStantardPixelBlock(HDC* hdc, int x, int y);
void DrawStantardPixelBlockWithIndexConverter(HDC* hdc, int x, int y);


int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                      _In_opt_ HINSTANCE hPrevInstance,
                      _In_ LPWSTR    lpCmdLine,
                      _In_ int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(lpCmdLine);

    // 在此处放置代码。

    // 初始化全局字符串
    LoadStringW(hInstance, IDS_APP_TITLE, szTitle, MAX_LOADSTRING);
    LoadStringW(hInstance, IDC_KITXINSTALLERFORWINDOWS, szWindowClass, MAX_LOADSTRING);
    MyRegisterClass(hInstance);

    // 执行应用程序初始化:
    if (!InitInstance(hInstance, nCmdShow))
    {
        return FALSE;
    }

    HACCEL hAccelTable = LoadAccelerators(hInstance, MAKEINTRESOURCE(IDC_KITXINSTALLERFORWINDOWS));

    MSG msg;

    // 主消息循环:
    while (GetMessage(&msg, nullptr, 0, 0))
    {
        if (!TranslateAccelerator(msg.hwnd, hAccelTable, &msg))
        {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
    }

    return (int)msg.wParam;
}



//
//  函数: MyRegisterClass()
//
//  目标: 注册窗口类。
//
ATOM MyRegisterClass(HINSTANCE hInstance)
{
    WNDCLASSEXW wcex{};

    wcex.cbSize = sizeof(WNDCLASSEX);

    wcex.style = CS_HREDRAW | CS_VREDRAW;
    wcex.lpfnWndProc = WndProc;
    wcex.cbClsExtra = 0;
    wcex.cbWndExtra = 0;
    wcex.hInstance = hInstance;
    wcex.hIcon = LoadIcon(hInstance, MAKEINTRESOURCE(IDI_KITXINSTALLERFORWINDOWS));
    wcex.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wcex.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wcex.lpszMenuName = nullptr;
    /*wcex.lpszMenuName = MAKEINTRESOURCEW(IDC_KITXINSTALLERFORWINDOWS);*/
    wcex.lpszClassName = szWindowClass;
    wcex.hIconSm = LoadIcon(wcex.hInstance, MAKEINTRESOURCE(IDI_SMALL));

    return RegisterClassExW(&wcex);
}


// Controls
HWND btn_confirm_install;
HWND edit_install_path;
HWND progress_download;

//
//   函数: InitInstance(HINSTANCE, int)
//
//   目标: 保存实例句柄并创建主窗口
//
//   注释:
//
//        在此函数中，我们在全局变量中保存实例句柄并
//        创建和显示主程序窗口。
//
BOOL InitInstance(HINSTANCE hInstance, int nCmdShow)
{
    hInst = hInstance; // 将实例句柄存储在全局变量中

    HWND hWnd = CreateWindowW(szWindowClass, L"KitX Installer | KitX 安装向导",
                              WS_OVERLAPPEDWINDOW ^ WS_THICKFRAME ^ WS_MAXIMIZEBOX,
                              (ScreenX - 800) / 2, (ScreenY - 600) / 2, 800, 600,
                              nullptr, nullptr, hInstance, nullptr);

    btn_confirm_install = CreateWindow(
        TEXT("button"),  // Predefined class; Unicode assumed 
        TEXT("Install | 安装"),      // Button text 
        WS_TABSTOP | WS_VISIBLE | WS_CHILD | BS_DEFPUSHBUTTON,  // Styles 
        340,         // x position 
        500,         // y position 
        120,        // Button width
        30,        // Button height
        hWnd,     // Parent window
        (HMENU)IDB_INSTALL,       // No menu.
        (HINSTANCE)GetWindowLongPtr(hWnd, GWLP_HINSTANCE),
        NULL);      // Pointer not needed.

    edit_install_path = CreateWindow(
        TEXT("edit"), TEXT("C:\\Program Files\\Crequency\\KitX\\"),
        WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
        230, 450, 340, 30, hWnd, (HMENU)IDTB_Path, NULL, NULL);

    /*progress_download = CreateWindowEx(30, PROGRESS_CLASS, (LPTSTR)NULL, WS_CHILD | WS_VISIBLE,
                                       0, 800, 800, 30, hWnd, (HMENU)0, NULL, NULL);*/

    LOGFONT lf{};//声明一个逻辑字体，因为创建太痛苦了，15个字段都要设置，要人的命
    HFONT hFont;//这里是我们创建的字体哦
    GetObject(GetStockObject(DEFAULT_GUI_FONT), sizeof(lf), &lf);//得到设备字体，同时填充逻辑字体结构，这样大大减少了代码DEFAULT_GUI_FONT 这个API上有的，还有SYSTEM_FONT 自己去查查

    lf.lfHeight = 14;

    //开始创建字体，很简单，创建一个逻辑字体就可以
    hFont = CreateFontIndirect(&lf);//哈哈，这里就是上面的那个结构
    SendMessage(btn_confirm_install, WM_SETFONT, (WPARAM)hFont, (LPARAM)TRUE); //这样的就设置控件的字体了啊
    DeleteObject(GetStockObject(DEFAULT_GUI_FONT));

    LOGFONT font{};
    font.lfHeight = 24;
    font.lfWidth = 0;
    font.lfEscapement = 0;
    font.lfOrientation = 0;
    font.lfWeight = FW_REGULAR;
    font.lfItalic = false;
    font.lfUnderline = false;
    font.lfStrikeOut = false;
    font.lfEscapement = 0;
    font.lfOrientation = 0;
    font.lfOutPrecision = OUT_DEFAULT_PRECIS;
    font.lfClipPrecision = CLIP_STROKE_PRECIS | CLIP_MASK | CLIP_TT_ALWAYS | CLIP_LH_ANGLES;
    font.lfQuality = ANTIALIASED_QUALITY;
    font.lfPitchAndFamily = VARIABLE_PITCH | FF_DONTCARE;

    HFONT heFont = ::CreateFontIndirect(&font);
    SendMessage(edit_install_path, WM_SETFONT, (WPARAM)heFont, TRUE);

    if (!hWnd)
    {
        return FALSE;
    }

    ShowWindow(hWnd, nCmdShow);
    UpdateWindow(hWnd);

    return TRUE;
}

//
//  函数: WndProc(HWND, UINT, WPARAM, LPARAM)
//
//  目标: 处理主窗口的消息。
//
//  WM_COMMAND  - 处理应用程序菜单
//  WM_PAINT    - 绘制主窗口
//  WM_DESTROY  - 发送退出消息并返回
//
//
LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
        case WM_PAINT:
        {
            PAINTSTRUCT ps;
            HDC hdc = BeginPaint(hWnd, &ps);
            // 在此处添加使用 hdc 的任何绘图代码...

            // 绘制背景
            /*for (int i = 0; i < rand() % 30 + 10; ++i)
                DrawBackground(&hdc);*/

            //OnEraseBkGnd(hWnd);

            PixelBackground(&hdc);

            EndPaint(hWnd, &ps);
        }
        break;
        case WM_COMMAND:
        {
            switch (LOWORD(wParam))
            {
                case IDB_INSTALL:
                    char sizeBuffer[100];
                    int len = GetWindowTextLength(edit_install_path);
                    if (GetWindowTextA(edit_install_path, sizeBuffer, len + 1) < 1)
                    {
                        MessageBox(hWnd, TEXT("Path can't be NULL.\r\n路径不能为空"), TEXT("Error | 错误"), MB_OK | MB_ICONINFORMATION);
                    }
                    else
                    {
                        if (0 == _access(sizeBuffer, 0))
                        {
                            //MessageBox(hWnd, TEXT("Will install to | 即将安装到:\r\n"), TEXT("Tip | 提示"), MB_OK | MB_ICONINFORMATION);

                        }
                        else
                        {
                            int ret = _mkdir(sizeBuffer);
                            if (0 == _access(sizeBuffer, 0))
                            {
                                MessageBox(hWnd, TEXT("创建路径成功"), TEXT("Tip | 提示"), MB_OK | MB_ICONINFORMATION);
                            }
                            else
                            {
                                MessageBox(hWnd, TEXT("创建路径失败"), TEXT("Tip | 提示"), MB_OK | MB_ICONINFORMATION);
                            }
                        }
                    }
                    break;
            }
        }
        break;
        case WM_DESTROY:

            PostQuitMessage(0);
            break;
        case WM_ERASEBKGND:
        {

        }
        break;
        default:
            return DefWindowProc(hWnd, message, wParam, lParam);
    }
    return 0;
}

void DrawBackground(HDC* hdc)
{
    DrawRectangle(hdc, rand() % 700, rand() % 500, rand() % 100 + 30, rand() % 100 + 30, rand() % 30);
}

void DrawRectangle(HDC* hdc, int x, int y, int width, int height, int thinkness)
{
    Rectangle(*hdc, x, y, x + width, y + height);
    if (thinkness > 1)
    {
        for (int i = 0; i < thinkness; ++i)
        {
            Rectangle(*hdc, x + i, y + i, x + width - i, y + height - i);
        }
    }
}

void OnEraseBkGnd(HWND hwnd)
{
    /* Vars */
    HDC dc; /* Standard Device Context; used to do the painting */

    /* rect = Client Rect of the window;
    Temp = Temparory rect tangle for the color bands */
    RECT rect, temp{};
    HBRUSH color; /* A brush to do the painting with */

    /* Get the dc for the wnd */
    dc = GetDC(hwnd);

    /* Get the client rect */
    GetClientRect(hwnd, &rect);

    /* Start color; Change the R,G,B values
    to the color of your choice */
    int r1 = 218, g1 = 189, b1 = 61;

    /* End Color; Change the R,G,B values
    to the color of your choice */
    int r2 = 214, g2 = 95, b2 = 130;

    /* loop to create the gradient */
    for (int i = 0; i < rect.right; i++)
    {
        /* Color ref. for the gradient */
        int r, g, b;
        /* Determine the colors */
        r = r1 + (i * (r2 - r1) / rect.right);
        g = g1 + (i * (g2 - g1) / rect.right);
        b = b1 + (i * (b2 - b1) / rect.right);

        /* Fill in the rectangle information */

        /* The uper left point of the rectangle
        being painted; uses i as the starting point*/
        temp.left = i;
        /* Upeer Y cord. Always start at the top */
        temp.top = 0;
        /* Okay heres the key part,
        create a rectangle thats 1 pixel wide */
        temp.right = i + 1;
        /* Height of the rectangle */
        temp.bottom = rect.bottom;

        /* Create a brush to draw with;
        these colors are randomized */
        color = CreateSolidBrush(RGB(r, g, b));

        /* Finally fill in the rectangle */
        FillRect(dc, &temp, color);
    }
}

void PixelBackground(HDC* hdc)
{
    bool DrawIt[10][24] = {
        1,1,1,0,0,0,1,1,1,0,0,0,0,0,1,1,1,1,1,1,0,1,1,1,
        0,1,1,0,0,1,1,1,0,0,0,0,0,0,1,1,1,1,1,0,0,1,1,0,
        0,1,1,0,1,1,1,0,0,0,0,0,0,0,0,1,1,1,0,0,0,1,0,0,
        0,1,1,1,1,1,0,0,0,1,0,0,1,0,0,0,1,1,1,0,1,0,0,0,
        0,1,1,1,1,0,0,0,0,0,0,0,1,0,0,0,0,1,1,1,0,0,0,0,
        0,1,1,1,1,1,0,0,0,1,0,1,1,1,0,0,0,0,1,1,1,0,0,0,
        0,1,1,1,1,1,1,0,1,1,0,0,1,0,0,0,0,1,0,1,1,1,0,0,
        0,1,1,0,1,1,1,1,0,1,0,0,1,0,0,0,1,0,0,0,1,1,1,0,
        0,1,1,0,0,1,1,1,1,1,0,1,1,0,0,1,1,0,0,1,1,1,1,1,
        1,1,1,1,0,0,1,1,1,1,1,0,1,1,1,1,1,0,1,1,1,1,1,1
    };
    for (int i = 0; i < 10; ++i)
        for (int j = 0; j < 24; ++j)
            if (DrawIt[i][j])
                DrawStantardPixelBlockWithIndexConverter(hdc, j, i + 2);
}

void DrawStantardPixelBlockWithIndexConverter(HDC* hdc, int x, int y)
{
    DrawStantardPixelBlock(hdc, x * 30 + 30, y * 30);
}

void DrawStantardPixelBlock(HDC* hdc, int x, int y)
{
    DrawBlockWithShadow(hdc, x, y, 30, 2, 2, 1);
}

void DrawBlock(HDC* hdc, int x, int y, int size)
{
    RECT rect{};
    rect.left = x;
    rect.right = x + size;
    rect.top = y;
    rect.bottom = y + size;
    HBRUSH color = CreateSolidBrush(RGB(0, 0, 0));
    FillRect(*hdc, &rect, color);
}


void DrawBlockWithShadow(HDC* hdc, int x, int y, int size, int offset, int shadowSize, int breakness)
{
    RECT rect{}, shadow1{}, shadow2{}, shadowCover{};
    rect.left = x;
    rect.right = x + size - 2;
    rect.top = y;
    rect.bottom = y + size - 2;

    shadow1.left = rect.right;
    shadow1.right = rect.right + shadowSize;
    shadow1.top = rect.top + offset;
    shadow1.bottom = rect.bottom;

    shadow2.left = rect.left + offset;
    shadow2.right = rect.right;
    shadow2.top = rect.bottom;
    shadow2.bottom = rect.bottom + shadowSize;

    shadowCover.left = rect.right;
    shadowCover.right = rect.right + shadowSize - breakness;
    shadowCover.top = rect.bottom;
    shadowCover.bottom = rect.bottom + shadowSize - breakness;

    HBRUSH color = CreateSolidBrush(RGB(0, 0, 0));
    HBRUSH shadowc1 = CreateSolidBrush(RGB(150, 150, 150));
    HBRUSH shadowc2 = CreateSolidBrush(RGB(90, 90, 90));
    HBRUSH shadowcc = CreateSolidBrush(RGB(130, 130, 130));

    FillRect(*hdc, &rect, color);
    FillRect(*hdc, &shadow1, shadowc1);
    FillRect(*hdc, &shadow2, shadowc2);
    FillRect(*hdc, &shadowCover, shadowcc);
}


// 从左到右依次判断文件夹是否存在,不存在就创建
// example: /home/root/mkdir/1/2/3/4/
// 注意:最后一个如果是文件夹的话,需要加上 '\' 或者 '/'
int createDirectory(const std::string& directoryPath)
{
    int dirPathLen = directoryPath.length();
    if (dirPathLen > 220)
    {
        return -1;
    }
    char tmpDirPath[220] = { 0 };
    for (int i = 0; i < dirPathLen; ++i)
    {
        tmpDirPath[i] = directoryPath[i];
        if (tmpDirPath[i] == '\\' || tmpDirPath[i] == '/')
        {
            if (_access(tmpDirPath, 0) != 0)
            {
                int ret = _mkdir(tmpDirPath);
                if (ret != 0)
                {
                    return ret;
                }
            }
        }
    }
    return 0;
}
















//===============================================================================================================//
