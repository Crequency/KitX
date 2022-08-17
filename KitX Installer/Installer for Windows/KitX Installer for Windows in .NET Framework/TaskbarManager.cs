using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KitX_Installer_for_Windows_in.NET_Framework
{
    /// <summary>
    /// Represents an instance of the Windows taskbar
    /// </summary>
    public static class TaskbarManager
    {
        /// <summary>
        /// Sets the handle of the window whose taskbar button will be used
        /// to display progress.
        /// </summary>
        private static readonly IntPtr ownerHandle = IntPtr.Zero;

        static TaskbarManager()
        {
            var currentProcess = Process.GetCurrentProcess();
            if (currentProcess != null && currentProcess.MainWindowHandle != IntPtr.Zero)
                ownerHandle = currentProcess.MainWindowHandle;
        }

        /// <summary>
        /// Indicates whether this feature is supported on the current platform.
        /// </summary>
        private static bool IsPlatformSupported
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.CompareTo(new Version(6, 1)) >= 0; }
        }

        /// <summary>
        /// Displays or updates a progress bar hosted in a taskbar button of the main application window 
        /// to show the specific percentage completed of the full operation.
        /// </summary>
        /// <param name="currentValue">An application-defined value that indicates the proportion of the operation that has been completed at the time the method is called.</param>
        /// <param name="maximumValue">An application-defined value that specifies the value currentValue will have when the operation is complete.</param>
        public static void SetProgressValue(int currentValue, int maximumValue)
        {
            if (IsPlatformSupported && ownerHandle != IntPtr.Zero)
                TaskbarList.Instance.SetProgressValue(
                    ownerHandle,
                    Convert.ToUInt32(currentValue),
                    Convert.ToUInt32(maximumValue));
        }

        /// <summary>
        /// Displays or updates a progress bar hosted in a taskbar button of the given window handle 
        /// to show the specific percentage completed of the full operation.
        /// </summary>
        /// <param name="windowHandle">The handle of the window whose associated taskbar button is being used as a progress indicator.
        /// This window belong to a calling process associated with the button's application and must be already loaded.</param>
        /// <param name="currentValue">An application-defined value that indicates the proportion of the operation that has been completed at the time the method is called.</param>
        /// <param name="maximumValue">An application-defined value that specifies the value currentValue will have when the operation is complete.</param>
        public static void SetProgressValue(int currentValue, int maximumValue, IntPtr windowHandle)
        {
            if (IsPlatformSupported)
                TaskbarList.Instance.SetProgressValue(
                    windowHandle,
                    Convert.ToUInt32(currentValue),
                    Convert.ToUInt32(maximumValue));
        }

        /// <summary>
        /// Sets the type and state of the progress indicator displayed on a taskbar button of the main application window.
        /// </summary>
        /// <param name="state">Progress state of the progress button</param>
        public static void SetProgressState(TaskbarProgressBarState state)
        {
            if (IsPlatformSupported && ownerHandle != IntPtr.Zero)
                TaskbarList.Instance.SetProgressState(ownerHandle, (TaskbarProgressBarStatus)state);
        }

        /// <summary>
        /// Sets the type and state of the progress indicator displayed on a taskbar button 
        /// of the given window handle 
        /// </summary>
        /// <param name="windowHandle">The handle of the window whose associated taskbar button is being used as a progress indicator.
        /// This window belong to a calling process associated with the button's application and must be already loaded.</param>
        /// <param name="state">Progress state of the progress button</param>
        public static void SetProgressState(TaskbarProgressBarState state, IntPtr windowHandle)
        {
            if (IsPlatformSupported)
                TaskbarList.Instance.SetProgressState(windowHandle, (TaskbarProgressBarStatus)state);
        }
    }


    /// <summary>
    /// Represents the thumbnail progress bar state.
    /// </summary>
    public enum TaskbarProgressBarState
    {
        /// <summary>
        /// No progress is displayed.
        /// </summary>
        NoProgress = 0,

        /// <summary>
        /// The progress is indeterminate (marquee).
        /// </summary>
        Indeterminate = 0x1,

        /// <summary>
        /// Normal progress is displayed.
        /// </summary>
        Normal = 0x2,

        /// <summary>
        /// An error occurred (red).
        /// </summary>
        Error = 0x4,

        /// <summary>
        /// The operation is paused (yellow).
        /// </summary>
        Paused = 0x8
    }

    /// <summary>
    /// Provides internal access to the functions provided by the ITaskbarList4 interface,
    /// without being forced to refer to it through another singleton.
    /// </summary>
    internal static class TaskbarList
    {
        private static readonly object _syncLock = new object();

        private static ITaskbarList4 _taskbarList;
        internal static ITaskbarList4 Instance
        {
            get
            {
                if (_taskbarList == null)
                {
                    lock (_syncLock)
                    {
                        if (_taskbarList == null)
                        {
                            _taskbarList = (ITaskbarList4)new CTaskbarList();
                            _taskbarList.HrInit();
                        }
                    }
                }

                return _taskbarList;
            }
        }
    }

    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    [ComImport()]
    internal class CTaskbarList { }


    [ComImport()]
    [Guid("c43dc798-95d1-4bea-9030-bb99e2983a1a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITaskbarList4
    {
        // ITaskbarList
        [PreserveSig]
        void HrInit();
        [PreserveSig]
        void AddTab(IntPtr hwnd);
        [PreserveSig]
        void DeleteTab(IntPtr hwnd);
        [PreserveSig]
        void ActivateTab(IntPtr hwnd);
        [PreserveSig]
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig]
        void MarkFullscreenWindow(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        [PreserveSig]
        void SetProgressValue(IntPtr hwnd, UInt64 ullCompleted, UInt64 ullTotal);
        [PreserveSig]
        void SetProgressState(IntPtr hwnd, TaskbarProgressBarStatus tbpFlags);
        [PreserveSig]
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        [PreserveSig]
        void UnregisterTab(IntPtr hwndTab);
        [PreserveSig]
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        [PreserveSig]
        void SetTabActive(IntPtr hwndTab, IntPtr hwndInsertBefore, uint dwReserved);
        [PreserveSig]
        HResult ThumbBarAddButtons(
            IntPtr hwnd,
            uint cButtons,
            [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);
        [PreserveSig]
        HResult ThumbBarUpdateButtons(
            IntPtr hwnd,
            uint cButtons,
            [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);
        [PreserveSig]
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        [PreserveSig]
        void SetOverlayIcon(
          IntPtr hwnd,
          IntPtr hIcon,
          [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        [PreserveSig]
        void SetThumbnailTooltip(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        [PreserveSig]
        void SetThumbnailClip(
            IntPtr hwnd,
            IntPtr prcClip);

        // ITaskbarList4
        void SetTabProperties(IntPtr hwndTab, SetTabPropertiesOption stpFlags);
    }

    internal enum TaskbarProgressBarStatus
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    internal enum ThumbButtonMask
    {
        Bitmap = 0x1,
        Icon = 0x2,
        Tooltip = 0x4,
        THB_FLAGS = 0x8
    }

    [Flags]
    internal enum ThumbButtonOptions
    {
        Enabled = 0x00000000,
        Disabled = 0x00000001,
        DismissOnClick = 0x00000002,
        NoBackground = 0x00000004,
        Hidden = 0x00000008,
        NonInteractive = 0x00000010
    }

    internal enum SetTabPropertiesOption
    {
        None = 0x0,
        UseAppThumbnailAlways = 0x1,
        UseAppThumbnailWhenActive = 0x2,
        UseAppPeekAlways = 0x4,
        UseAppPeekWhenActive = 0x8
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct ThumbButton
    {
        /// <summary>
        /// WPARAM value for a THUMBBUTTON being clicked.
        /// </summary>
        internal const int Clicked = 0x1800;

        [MarshalAs(UnmanagedType.U4)]
        internal ThumbButtonMask Mask;
        internal uint Id;
        internal uint Bitmap;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string Tip;
        [MarshalAs(UnmanagedType.U4)]
        internal ThumbButtonOptions Flags;
    }

    /// <summary>
    /// HRESULT Wrapper
    /// </summary>
    public enum HResult
    {
        /// <summary>
        /// S_OK
        /// </summary>
        Ok = 0x0000,

        /// <summary>
        /// S_FALSE
        /// </summary>
        False = 0x0001,

        /// <summary>
        /// E_INVALIDARG
        /// </summary>
        InvalidArguments = unchecked((int)0x80070057),

        /// <summary>
        /// E_OUTOFMEMORY
        /// </summary>
        OutOfMemory = unchecked((int)0x8007000E),

        /// <summary>
        /// E_NOINTERFACE
        /// </summary>
        NoInterface = unchecked((int)0x80004002),

        /// <summary>
        /// E_FAIL
        /// </summary>
        Fail = unchecked((int)0x80004005),

        /// <summary>
        /// E_ELEMENTNOTFOUND
        /// </summary>
        ElementNotFound = unchecked((int)0x80070490),

        /// <summary>
        /// TYPE_E_ELEMENTNOTFOUND
        /// </summary>
        TypeElementNotFound = unchecked((int)0x8002802B),

        /// <summary>
        /// NO_OBJECT
        /// </summary>
        NoObject = unchecked((int)0x800401E5),

        /// <summary>
        /// Win32 Error code: ERROR_CANCELLED
        /// </summary>
        Win32ErrorCanceled = 1223,

        /// <summary>
        /// ERROR_CANCELLED
        /// </summary>
        Canceled = unchecked((int)0x800704C7),

        /// <summary>
        /// The requested resource is in use
        /// </summary>
        ResourceInUse = unchecked((int)0x800700AA),

        /// <summary>
        /// The requested resources is read-only.
        /// </summary>
        AccessDenied = unchecked((int)0x80030005)
    }
}
