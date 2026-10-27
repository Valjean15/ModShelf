using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// The Windows Runtime calls that show a toast, made through combase rather than a projection, since Platform builds
/// for every system. Each method is called through its interface's table of functions, at the slot the Windows SDK
/// declares it in: after IUnknown's three and IInspectable's three.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ToastInterop
{
    private const string Combase = "combase.dll";

    // RO_INIT_MULTITHREADED; and RPC_E_CHANGED_MODE, when the thread already joined a single-threaded apartment, which
    // the Windows Runtime works with too.
    private const int RoInitMultithreaded = 1;
    private const int RpcEChangedMode = unchecked((int)0x80010106);

    private const int FirstMethod = 6;

    // NotificationSetting.Enabled; the other values say who turned notices off.
    private const int NotificationsEnabled = 0;

    private static readonly Guid XmlDocumentIoId = new("6cd0e74e-ee65-4489-9ebf-ca43e87ba637");
    private static readonly Guid XmlDocumentId = new("f7f3a506-1e87-42d6-bcfb-b8c809fa5494");
    private static readonly Guid ToastNotificationFactoryId = new("04124b20-82c6-4229-b109-fd9ed4662b53");
    private static readonly Guid ToastNotificationManagerStaticsId = new("50ac103f-d235-4598-bbef-98fe4d1a3ad4");

    /// <summary>
    /// Shows a toast made of <paramref name="toastXml"/>, as the app <paramref name="applicationId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Windows has notices turned off for the app or the user.</exception>
    /// <exception cref="COMException">A Windows Runtime call failed.</exception>
    public static void Show(string applicationId, string toastXml)
    {
        Initialize();
        IntPtr notifier = CreateNotifier(applicationId);
        try
        {
            // Asking can fail for an app Windows hasn't seen yet; then the toast is tried anyway.
            if (TryGetSetting(notifier) is { } setting and not NotificationsEnabled)
            {
                throw new InvalidOperationException($"Windows has notices turned off {Who(setting)}.");
            }

            IntPtr toast = CreateToast(toastXml);
            try
            {
                // IToastNotifier.Show(ToastNotification)
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Method(notifier, 0))(notifier, toast));
            }
            finally
            {
                _ = Marshal.Release(toast);
            }
        }
        finally
        {
            _ = Marshal.Release(notifier);
        }
    }

    /// <summary>
    /// Joins this thread to the Windows Runtime, unless it has joined already.
    /// </summary>
    internal static void Initialize()
    {
        int result = RoInitialize(RoInitMultithreaded);
        if (result != RpcEChangedMode)
        {
            Check(result);
        }
    }

    /// <summary>
    /// The toast notifier of <paramref name="applicationId"/>; release it when done.
    /// </summary>
    internal static IntPtr CreateNotifier(string applicationId)
    {
        IntPtr manager = GetFactory("Windows.UI.Notifications.ToastNotificationManager", ToastNotificationManagerStaticsId);
        try
        {
            IntPtr id = CreateString(applicationId);
            try
            {
                // IToastNotificationManagerStatics.CreateToastNotifierWithId(HSTRING), after CreateToastNotifier()
                IntPtr notifier;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Method(manager, 1))(manager, id, &notifier));
                return notifier;
            }
            finally
            {
                _ = WindowsDeleteString(id);
            }
        }
        finally
        {
            _ = Marshal.Release(manager);
        }
    }

    /// <summary>
    /// A toast made of <paramref name="toastXml"/>; release it when done.
    /// </summary>
    internal static IntPtr CreateToast(string toastXml)
    {
        IntPtr document = Activate("Windows.Data.Xml.Dom.XmlDocument");
        try
        {
            IntPtr io = QueryInterface(document, XmlDocumentIoId);
            try
            {
                IntPtr xml = CreateString(toastXml);
                try
                {
                    // IXmlDocumentIO.LoadXml(HSTRING)
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Method(io, 0))(io, xml));
                }
                finally
                {
                    _ = WindowsDeleteString(xml);
                }
            }
            finally
            {
                _ = Marshal.Release(io);
            }

            IntPtr content = QueryInterface(document, XmlDocumentId);
            try
            {
                IntPtr factory = GetFactory("Windows.UI.Notifications.ToastNotification", ToastNotificationFactoryId);
                try
                {
                    // IToastNotificationFactory.CreateToastNotification(XmlDocument)
                    IntPtr toast;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Method(factory, 0))(factory, content, &toast));
                    return toast;
                }
                finally
                {
                    _ = Marshal.Release(factory);
                }
            }
            finally
            {
                _ = Marshal.Release(content);
            }
        }
        finally
        {
            _ = Marshal.Release(document);
        }
    }

    /// <summary>
    /// Whether Windows lets <paramref name="notifier"/>'s toasts show, as its NotificationSetting; null when it can't say.
    /// </summary>
    internal static int? TryGetSetting(IntPtr notifier)
    {
        // IToastNotifier.Setting, after Show and Hide
        int setting;
        int result = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Method(notifier, 2))(notifier, &setting);
        return result < 0 ? null : setting;
    }

    private static string Who(int setting) => setting switch
    {
        1 => "for ModShelf (Settings > System > Notifications)",
        2 => "for this user (Settings > System > Notifications)",
        3 => "by group policy",
        4 => "by the app's manifest",
        _ => $"(setting {setting})",
    };

    /// <summary>
    /// The function at slot <paramref name="index"/> of <paramref name="instance"/>'s own methods, after IInspectable's.
    /// </summary>
    private static IntPtr Method(IntPtr instance, int index) => (*(IntPtr**)instance)[FirstMethod + index];

    private static IntPtr Activate(string className)
    {
        IntPtr name = CreateString(className);
        try
        {
            Check(RoActivateInstance(name, out IntPtr instance));
            return instance;
        }
        finally
        {
            _ = WindowsDeleteString(name);
        }
    }

    private static IntPtr GetFactory(string className, Guid interfaceId)
    {
        IntPtr name = CreateString(className);
        try
        {
            Check(RoGetActivationFactory(name, in interfaceId, out IntPtr factory));
            return factory;
        }
        finally
        {
            _ = WindowsDeleteString(name);
        }
    }

    private static IntPtr QueryInterface(IntPtr instance, Guid interfaceId)
    {
        Check(Marshal.QueryInterface(instance, in interfaceId, out IntPtr result));
        return result;
    }

    private static IntPtr CreateString(string text)
    {
        Check(WindowsCreateString(text, text.Length, out IntPtr result));
        return result;
    }

    private static void Check(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [LibraryImport(Combase)]
    private static partial int RoInitialize(int initType);

    [LibraryImport(Combase)]
    private static partial int RoActivateInstance(IntPtr activatableClassId, out IntPtr instance);

    [LibraryImport(Combase)]
    private static partial int RoGetActivationFactory(IntPtr activatableClassId, in Guid interfaceId, out IntPtr factory);

    [LibraryImport(Combase, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, int length, out IntPtr text);

    [LibraryImport(Combase)]
    private static partial int WindowsDeleteString(IntPtr text);
}
