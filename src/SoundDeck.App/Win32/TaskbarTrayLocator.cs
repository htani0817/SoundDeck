namespace SoundDeck.App.Win32;

/// <summary>
/// サブモニターのタスクバーにある時計など（通知領域の部品）の左端を、UI オートメーションで調べる。
/// Windows 11 のサブモニターのタスクバーは中身が XAML で、時計の位置を Win32 のウィンドウからは取れないため。
/// COM の MTA で初期化したスレッドから使うこと。
/// </summary>
internal sealed unsafe class TaskbarTrayLocator : IDisposable
{
    private const uint ClsctxInprocServer = 0x1;
    private const int TreeScopeDescendants = 0x4;

    private static readonly Guid s_clsidCuiAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid s_iidIuiAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    // IUIAutomation / IUIAutomationElement / IUIAutomationElementArray の vtable の番号
    private const int AutomationElementFromHandle = 6;
    private const int AutomationCreateTrueCondition = 21;
    private const int ElementFindAll = 6;
    private const int ElementGetCurrentClassName = 30;
    private const int ElementGetCurrentBoundingRectangle = 43;
    private const int ArrayGetLength = 3;
    private const int ArrayGetElement = 4;

    private IntPtr _automation;
    private IntPtr _trueCondition;

    public TaskbarTrayLocator()
    {
        if (NativeMethods.CoCreateInstance(s_clsidCuiAutomation, IntPtr.Zero, ClsctxInprocServer, s_iidIuiAutomation, out _automation) < 0)
        {
            _automation = IntPtr.Zero;
            return;
        }

        IntPtr condition;
        if (Call(_automation, AutomationCreateTrueCondition, &condition) >= 0)
        {
            _trueCondition = condition;
        }
    }

    /// <summary>通知領域の部品（クラス名が SystemTray. で始まるもの）の左端。見つからなければ null。</summary>
    public int? FindTrayLeft(IntPtr taskbar)
    {
        if (_automation == IntPtr.Zero || _trueCondition == IntPtr.Zero)
        {
            return null;
        }

        IntPtr element;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VTable(_automation)[AutomationElementFromHandle])(_automation, taskbar, &element) < 0 || element == IntPtr.Zero)
        {
            return null;
        }

        int? left = null;
        try
        {
            IntPtr array;
            if (((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr, IntPtr*, int>)VTable(element)[ElementFindAll])(element, TreeScopeDescendants, _trueCondition, &array) < 0 || array == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                int length;
                Call(array, ArrayGetLength, &length);
                for (var i = 0; i < length; i++)
                {
                    IntPtr item;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)VTable(array)[ArrayGetElement])(array, i, &item) < 0 || item == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        if (!GetClassName(item).StartsWith("SystemTray.", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        Rect rect;
                        if (Call(item, ElementGetCurrentBoundingRectangle, &rect) >= 0 && rect.Width > 0)
                        {
                            left = left is { } current ? Math.Min(current, rect.Left) : rect.Left;
                        }
                    }
                    finally
                    {
                        Release(item);
                    }
                }
            }
            finally
            {
                Release(array);
            }
        }
        finally
        {
            Release(element);
        }

        return left;
    }

    public void Dispose()
    {
        Release(_trueCondition);
        Release(_automation);
        _trueCondition = IntPtr.Zero;
        _automation = IntPtr.Zero;
    }

    private static string GetClassName(IntPtr element)
    {
        IntPtr bstr;
        if (Call(element, ElementGetCurrentClassName, &bstr) < 0 || bstr == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            return new string((char*)bstr);
        }
        finally
        {
            NativeMethods.SysFreeString(bstr);
        }
    }

    private static IntPtr* VTable(IntPtr instance) => *(IntPtr**)instance;

    /// <summary>引数がポインタ 1 つだけのメソッドを呼ぶ。</summary>
    private static int Call(IntPtr instance, int slot, void* result) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, void*, int>)VTable(instance)[slot])(instance, result);

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance)[2])(instance);
        }
    }
}
