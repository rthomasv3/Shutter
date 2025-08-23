using System;
using System.Runtime.InteropServices;

namespace AllPlatformScreenshots;

internal static class DBusNative
{
    private const string DBUS_LIB = "libdbus-1";

    // DBus connection types
    public const int DBUS_BUS_SESSION = 0;
    public const int DBUS_BUS_SYSTEM = 1;

    // DBus message types
    public const int DBUS_MESSAGE_TYPE_METHOD_CALL = 1;
    public const int DBUS_MESSAGE_TYPE_METHOD_RETURN = 2;
    public const int DBUS_MESSAGE_TYPE_ERROR = 3;
    public const int DBUS_MESSAGE_TYPE_SIGNAL = 4;

    // DBus types for arguments (using actual character values)
    public const int DBUS_TYPE_INVALID = 0;
    public const int DBUS_TYPE_BYTE = 121;        // 'y'
    public const int DBUS_TYPE_BOOLEAN = 98;      // 'b'
    public const int DBUS_TYPE_INT16 = 110;       // 'n'
    public const int DBUS_TYPE_UINT16 = 113;      // 'q'
    public const int DBUS_TYPE_INT32 = 105;       // 'i'
    public const int DBUS_TYPE_UINT32 = 117;      // 'u'
    public const int DBUS_TYPE_INT64 = 120;       // 'x'
    public const int DBUS_TYPE_UINT64 = 116;      // 't'
    public const int DBUS_TYPE_DOUBLE = 100;      // 'd'
    public const int DBUS_TYPE_STRING = 115;      // 's'
    public const int DBUS_TYPE_OBJECT_PATH = 111; // 'o'
    public const int DBUS_TYPE_SIGNATURE = 103;   // 'g'
    public const int DBUS_TYPE_ARRAY = 97;        // 'a'
    public const int DBUS_TYPE_VARIANT = 118;     // 'v'
    public const int DBUS_TYPE_STRUCT = 114;      // 'r'
    public const int DBUS_TYPE_DICT_ENTRY = 101;  // 'e'

    [StructLayout(LayoutKind.Sequential)]
    public struct DBusError
    {
        public IntPtr name;
        public IntPtr message;
        public uint dummy1;
        public uint dummy2;
        public uint dummy3;
        public uint dummy4;
        public uint dummy5;
        public IntPtr padding1;
    }

    [DllImport(DBUS_LIB)]
    public static extern IntPtr dbus_bus_get(int type, ref DBusError error);

    [DllImport(DBUS_LIB)]
    public static extern IntPtr dbus_bus_get(int type, IntPtr error);

    [DllImport(DBUS_LIB)]
    public static extern IntPtr dbus_message_new_method_call(
        string destination,
        string path,
        string interface_name,
        string method);

    [DllImport(DBUS_LIB)]
    public static extern IntPtr dbus_connection_send_with_reply_and_block(
        IntPtr connection,
        IntPtr message,
        int timeout_milliseconds,
        ref DBusError error);

    [DllImport(DBUS_LIB)]
    public static extern IntPtr dbus_connection_send_with_reply_and_block(
        IntPtr connection,
        IntPtr message,
        int timeout_milliseconds,
        IntPtr error);

    [DllImport(DBUS_LIB)]
    public static extern void dbus_message_unref(IntPtr message);

    [DllImport(DBUS_LIB)]
    public static extern void dbus_connection_unref(IntPtr connection);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_message_iter_init(IntPtr message, IntPtr iter);

    [DllImport(DBUS_LIB)]
    public static extern int dbus_message_iter_get_arg_type(IntPtr iter);

    [DllImport(DBUS_LIB)]
    public static extern void dbus_message_iter_get_basic(IntPtr iter, IntPtr value);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_message_iter_next(IntPtr iter);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_bus_add_match(
        IntPtr connection,
        string rule,
        ref DBusError error);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_bus_add_match(
        IntPtr connection,
        string rule,
        IntPtr error);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_connection_read_write_dispatch(
        IntPtr connection,
        int timeout_milliseconds);

    [DllImport(DBUS_LIB)]
    public static extern IntPtr dbus_connection_pop_message(IntPtr connection);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_message_is_signal(
        IntPtr message,
        string interface_name,
        string signal_name);

    [DllImport(DBUS_LIB)]
    public static extern void dbus_error_init(ref DBusError error);

    [DllImport(DBUS_LIB)]
    public static extern void dbus_error_free(ref DBusError error);

    [DllImport(DBUS_LIB)]
    public static extern void dbus_message_iter_init_append(IntPtr message, IntPtr iter);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_message_iter_append_basic(IntPtr iter, int type, IntPtr value);

    [DllImport(DBUS_LIB, CharSet = CharSet.Ansi)]
    public static extern bool dbus_message_iter_open_container(IntPtr iter, int type, string contained_signature, IntPtr sub_iter);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_message_iter_open_container(IntPtr iter, int type, IntPtr contained_signature, IntPtr sub_iter);

    [DllImport(DBUS_LIB)]
    public static extern bool dbus_message_iter_close_container(IntPtr iter, IntPtr sub_iter);

    // Additional method needed for parsing responses
    [DllImport(DBUS_LIB)]
    public static extern void dbus_message_iter_recurse(IntPtr iter, IntPtr sub_iter);
}
