// KitX Installer for Windows.cpp : 定义应用程序的入口点。
//

#include "framework.h"
#include "KitX Installer for Windows.h"

#pragma comment(linker,"/manifestdependency:\"type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")

#define MAX_LOADSTRING 100

#define ScreenX GetSystemMetrics(SM_CXSCREEN)
#define ScreenY GetSystemMetrics(SM_CYSCREEN)

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


int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
    _In_opt_ HINSTANCE hPrevInstance,
    _In_ LPWSTR    lpCmdLine,
    _In_ int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(lpCmdLine);

    // TODO: 在此处放置代码。

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
    WNDCLASSEXW wcex;

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
        L"BUTTON",  // Predefined class; Unicode assumed 
        L"Install | 安装",      // Button text 
        WS_TABSTOP | WS_VISIBLE | WS_CHILD | BS_DEFPUSHBUTTON,  // Styles 
        340,         // x position 
        500,         // y position 
        120,        // Button width
        30,        // Button height
        hWnd,     // Parent window
        NULL,       // No menu.
        (HINSTANCE)GetWindowLongPtr(hWnd, GWLP_HINSTANCE),
        NULL);      // Pointer not needed.

    edit_install_path = CreateWindow(
        L"edit", L"C:\\Program Files\\Crequency\\KitX\\", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL,
        230, 450, 340, 30, hWnd, NULL, NULL, NULL);


    LOGFONT lf;//声明一个逻辑字体，因为创建太痛苦了，15个字段都要设置，要人的命
    HFONT hFont;//这里是我们创建的字体哦
    GetObject(GetStockObject(DEFAULT_GUI_FONT), sizeof(lf), &lf);//得到设备字体，同时填充逻辑字体结构，这样大大减少了代码DEFAULT_GUI_FONT 这个API上有的，还有SYSTEM_FONT 自己去查查

    lf.lfHeight = 14;

    //开始创建字体，很简单，创建一个逻辑字体就可以
    hFont = CreateFontIndirect(&lf);//哈哈，这里就是上面的那个结构
    SendMessage(btn_confirm_install, WM_SETFONT, (WPARAM)hFont, (LPARAM)TRUE); //这样的就设置控件的字体了啊
    DeleteObject(GetStockObject(DEFAULT_GUI_FONT));

    LOGFONT font;
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

int AnimationFrameIndex = 0;

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

            // TODO: 在此处添加使用 hdc 的任何绘图代码...

            // 绘制背景
            //DrawBackground(&hdc);


            EndPaint(hWnd, &ps);
        }
        break;
        case WM_DESTROY:
            PostQuitMessage(0);
            break;
        default:
            return DefWindowProc(hWnd, message, wParam, lParam);
    }
    return 0;
}

void DrawBackground(HDC* hdc)
{
    DrawRectangle(hdc, 100, 100, 100, 100, AnimationFrameIndex % 100);
    ++AnimationFrameIndex;
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



























