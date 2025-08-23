using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Shutter.Abstractions;
using Shutter.Enums;
using Shutter.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Shutter;

internal class WaylandScreenshotService : IPlatformScreenshotService
{
    #region Fields

    // DBusMessageIter is typically 80-120 bytes depending on architecture
    private const int ITER_SIZE = 128;

    #endregion

    #region Public Methods

    public byte[] TakeScreenshot(ScreenshotOptions options)
    {
        // Wayland portal only supports fullscreen or interactive selection
        // All other targets fall back to fullscreen
        byte[] screenshotData = CaptureViaPortal(options);

        // Convert format if needed (portal returns PNG by default)
        if (options.Format == ImageFormat.Jpeg && screenshotData != null)
        {
            screenshotData = ConvertPngToJpeg(screenshotData, options.JpegQuality);
        }

        return screenshotData;
    }

    #endregion

    #region Private Methods

    private byte[] CaptureViaPortal(ScreenshotOptions options)
    {
        IntPtr connection = IntPtr.Zero;
        IntPtr message = IntPtr.Zero;
        IntPtr reply = IntPtr.Zero;
        byte[] result = null;

        try
        {
            Console.WriteLine("Connecting to DBus...");

            // 1. Connect to session bus with error handling
            DBusNative.DBusError error = new();
            DBusNative.dbus_error_init(ref error);

            connection = DBusNative.dbus_bus_get(DBusNative.DBUS_BUS_SESSION, ref error);
            if (connection == IntPtr.Zero)
            {
                string errorMsg = GetErrorMessage(ref error);
                DBusNative.dbus_error_free(ref error);
                throw new InvalidOperationException($"Failed to connect to DBus: {errorMsg}");
            }

            Console.WriteLine("Connected to DBus successfully");

            // 2. Create the Screenshot method call
            message = DBusNative.dbus_message_new_method_call(
                "org.freedesktop.portal.Desktop",
                "/org/freedesktop/portal/desktop",
                "org.freedesktop.portal.Screenshot",
                "Screenshot");

            if (message == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create DBus message");

            Console.WriteLine("Created Screenshot method call");

            // 3. Append arguments (parent_window and options dict)
            AppendScreenshotArguments(message, options);

            Console.WriteLine("Appended arguments, sending message...");

            // 4. Send the call and get response
            // Use the timeout from options, converting to milliseconds
            int timeoutMs = (int)options.Timeout.TotalMilliseconds;

            DBusNative.dbus_error_init(ref error);
            reply = DBusNative.dbus_connection_send_with_reply_and_block(
                connection, message, timeoutMs, ref error);

            if (reply == IntPtr.Zero)
            {
                string errorMsg = GetErrorMessage(ref error);
                DBusNative.dbus_error_free(ref error);
                throw new InvalidOperationException($"Failed to call Screenshot: {errorMsg}");
            }

            Console.WriteLine("Got reply from Screenshot method");

            // 5. Extract request handle from reply
            string requestHandle = ExtractRequestHandle(reply);
            Console.WriteLine($"Got request handle: {requestHandle}");

            // 6. Wait for the Response signal
            string screenshotPath = WaitForScreenshotResponse(connection, requestHandle, options.Timeout);

            if (string.IsNullOrEmpty(screenshotPath))
            {
                Console.WriteLine("User cancelled or no screenshot URI received");
                result = null;
            }
            else
            {
                // 7. Read the screenshot file
                string localPath = screenshotPath.Replace("file://", "");
                Console.WriteLine($"Reading screenshot from: {localPath}");

                if (!File.Exists(localPath))
                    throw new FileNotFoundException($"Screenshot file not found: {localPath}");

                result = File.ReadAllBytes(localPath);

                // Optional: Clean up the temporary file
                try
                {
                    File.Delete(localPath);
                    Console.WriteLine($"Deleted temporary file: {localPath}");
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        finally
        {
            if (reply != IntPtr.Zero)
                DBusNative.dbus_message_unref(reply);
            if (message != IntPtr.Zero)
                DBusNative.dbus_message_unref(message);
            if (connection != IntPtr.Zero)
                DBusNative.dbus_connection_unref(connection);
        }

        return result;
    }

    private unsafe void AppendScreenshotArguments(IntPtr message, ScreenshotOptions options)
    {
        // Allocate iterator for message
        IntPtr iter = Marshal.AllocHGlobal(ITER_SIZE);
        IntPtr dictIter = Marshal.AllocHGlobal(ITER_SIZE);

        try
        {
            // Clear the memory
            for (int i = 0; i < ITER_SIZE; i++)
            {
                Marshal.WriteByte(iter, i, 0);
                Marshal.WriteByte(dictIter, i, 0);
            }

            // Initialize the message iterator
            DBusNative.dbus_message_iter_init_append(message, iter);

            // Append parent_window (empty string for no parent)
            string parentWindow = "";
            byte[] parentWindowBytes = Encoding.UTF8.GetBytes(parentWindow + "\0");
            fixed (byte* parentWindowPtr = parentWindowBytes)
            {
                IntPtr strPtr = new(parentWindowPtr);
                IntPtr strPtrAddr = new(&strPtr);

                if (!DBusNative.dbus_message_iter_append_basic(iter, DBusNative.DBUS_TYPE_STRING, strPtrAddr))
                    throw new InvalidOperationException("Failed to append parent_window");
            }

            // Append options dictionary a{sv}
            string dictSignature = "{sv}";
            if (!DBusNative.dbus_message_iter_open_container(iter, DBusNative.DBUS_TYPE_ARRAY, dictSignature, dictIter))
                throw new InvalidOperationException("Failed to open dictionary container");

            // Add options to the dictionary
            // Set modal to false
            AddDictEntry(dictIter, "modal", false);

            // Set interactive based on options
            AddDictEntry(dictIter, "interactive", options.Interactive);

            if (!DBusNative.dbus_message_iter_close_container(iter, dictIter))
                throw new InvalidOperationException("Failed to close dictionary container");

            Console.WriteLine($"Successfully appended arguments (interactive: {options.Interactive})");
        }
        finally
        {
            Marshal.FreeHGlobal(dictIter);
            Marshal.FreeHGlobal(iter);
        }
    }

    private unsafe void AddDictEntry(IntPtr dictIter, string key, bool value)
    {
        IntPtr entryIter = Marshal.AllocHGlobal(ITER_SIZE);
        IntPtr variantIter = Marshal.AllocHGlobal(ITER_SIZE);

        try
        {
            // Clear the memory
            for (int i = 0; i < ITER_SIZE; i++)
            {
                Marshal.WriteByte(entryIter, i, 0);
                Marshal.WriteByte(variantIter, i, 0);
            }

            // Open dict entry container - NULL signature for dict entry
            if (!DBusNative.dbus_message_iter_open_container(dictIter, DBusNative.DBUS_TYPE_DICT_ENTRY, null, entryIter))
                throw new InvalidOperationException($"Failed to open dict entry for {key}");

            // Add key
            byte[] keyBytes = Encoding.UTF8.GetBytes(key + "\0");
            fixed (byte* keyPtr = keyBytes)
            {
                IntPtr strPtr = new(keyPtr);
                IntPtr strPtrAddr = new(&strPtr);

                if (!DBusNative.dbus_message_iter_append_basic(entryIter, DBusNative.DBUS_TYPE_STRING, strPtrAddr))
                    throw new InvalidOperationException($"Failed to append key {key}");
            }

            // Add variant value with signature "b" for boolean
            if (!DBusNative.dbus_message_iter_open_container(entryIter, DBusNative.DBUS_TYPE_VARIANT, "b", variantIter))
                throw new InvalidOperationException($"Failed to open variant for {key}");

            int boolValue = value ? 1 : 0;
            IntPtr boolPtr = new(&boolValue);

            if (!DBusNative.dbus_message_iter_append_basic(variantIter, DBusNative.DBUS_TYPE_BOOLEAN, boolPtr))
                throw new InvalidOperationException($"Failed to append boolean value for {key}");

            if (!DBusNative.dbus_message_iter_close_container(entryIter, variantIter))
                throw new InvalidOperationException($"Failed to close variant for {key}");

            if (!DBusNative.dbus_message_iter_close_container(dictIter, entryIter))
                throw new InvalidOperationException($"Failed to close dict entry for {key}");

            Console.WriteLine($"Added dict entry: {key} = {value}");
        }
        finally
        {
            Marshal.FreeHGlobal(variantIter);
            Marshal.FreeHGlobal(entryIter);
        }
    }

    private string ExtractRequestHandle(IntPtr reply)
    {
        IntPtr iter = Marshal.AllocHGlobal(ITER_SIZE);
        string handle = null;

        try
        {
            // Clear the memory
            for (int i = 0; i < ITER_SIZE; i++)
                Marshal.WriteByte(iter, i, 0);

            if (!DBusNative.dbus_message_iter_init(reply, iter))
                throw new InvalidOperationException("Reply has no arguments");

            int argType = DBusNative.dbus_message_iter_get_arg_type(iter);
            Console.WriteLine($"Reply arg type: {argType} (expecting {DBusNative.DBUS_TYPE_OBJECT_PATH})");

            if (argType != DBusNative.DBUS_TYPE_OBJECT_PATH)
                throw new InvalidOperationException($"Expected object path, got type {argType}");

            IntPtr handlePtr = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                DBusNative.dbus_message_iter_get_basic(iter, handlePtr);
                IntPtr handleStrPtr = Marshal.ReadIntPtr(handlePtr);
                handle = Marshal.PtrToStringAnsi(handleStrPtr);

                if (handle == null)
                    throw new InvalidOperationException("Failed to read request handle");
            }
            finally
            {
                Marshal.FreeHGlobal(handlePtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(iter);
        }

        return handle;
    }

    private string WaitForScreenshotResponse(IntPtr connection, string requestHandle, TimeSpan timeout)
    {
        // Add signal match for the Response signal
        string matchRule = $"type='signal',interface='org.freedesktop.portal.Request',member='Response',path='{requestHandle}'";
        string uri = null;

        DBusNative.DBusError error = new();
        DBusNative.dbus_error_init(ref error);

        if (!DBusNative.dbus_bus_add_match(connection, matchRule, ref error))
        {
            // This often fails but the signal is still received, so we just continue
            DBusNative.dbus_error_free(ref error);
        }

        Console.WriteLine($"Waiting for response on {requestHandle}...");

        // Wait for the signal
        DateTime startTime = DateTime.Now;

        while ((DateTime.Now - startTime) < timeout)
        {
            // Check for messages
            DBusNative.dbus_connection_read_write_dispatch(connection, 100);

            IntPtr message = DBusNative.dbus_connection_pop_message(connection);
            if (message != IntPtr.Zero)
            {
                try
                {
                    if (DBusNative.dbus_message_is_signal(message, "org.freedesktop.portal.Request", "Response"))
                    {
                        Console.WriteLine("Got Response signal!");
                        uri = ExtractScreenshotUri(message);
                        break;
                    }
                }
                finally
                {
                    DBusNative.dbus_message_unref(message);
                }
            }
        }

        if (uri == null && (DateTime.Now - startTime) >= timeout)
        {
            throw new TimeoutException($"Timeout waiting for screenshot response after {timeout.TotalSeconds} seconds");
        }

        return uri;
    }

    private string ExtractScreenshotUri(IntPtr message)
    {
        IntPtr iter = Marshal.AllocHGlobal(ITER_SIZE);
        IntPtr dictIter = Marshal.AllocHGlobal(ITER_SIZE);
        IntPtr entryIter = Marshal.AllocHGlobal(ITER_SIZE);
        IntPtr variantIter = Marshal.AllocHGlobal(ITER_SIZE);
        string uri = null;

        try
        {
            // Clear all iterators
            for (int i = 0; i < ITER_SIZE; i++)
            {
                Marshal.WriteByte(iter, i, 0);
                Marshal.WriteByte(dictIter, i, 0);
                Marshal.WriteByte(entryIter, i, 0);
                Marshal.WriteByte(variantIter, i, 0);
            }

            if (!DBusNative.dbus_message_iter_init(message, iter))
                throw new InvalidOperationException("Response has no arguments");

            // First argument is uint32 response code
            int responseCode = 0;
            if (DBusNative.dbus_message_iter_get_arg_type(iter) == DBusNative.DBUS_TYPE_UINT32)
            {
                IntPtr codePtr = Marshal.AllocHGlobal(4);
                try
                {
                    DBusNative.dbus_message_iter_get_basic(iter, codePtr);
                    responseCode = Marshal.ReadInt32(codePtr);
                    Console.WriteLine($"Response code: {responseCode}");
                }
                finally
                {
                    Marshal.FreeHGlobal(codePtr);
                }
            }

            if (responseCode != 0)
            {
                // User cancelled or error
                Console.WriteLine("User cancelled the screenshot");
                uri = null;
            }
            else
            {
                // Move to next argument (the dictionary)
                if (!DBusNative.dbus_message_iter_next(iter))
                    throw new InvalidOperationException("No results dictionary in response");

                // Should be an array (dictionary)
                if (DBusNative.dbus_message_iter_get_arg_type(iter) != DBusNative.DBUS_TYPE_ARRAY)
                    throw new InvalidOperationException("Expected array (dictionary) in response");

                // Recurse into the dictionary
                DBusNative.dbus_message_iter_recurse(iter, dictIter);

                // Iterate through dictionary entries
                while (DBusNative.dbus_message_iter_get_arg_type(dictIter) == DBusNative.DBUS_TYPE_DICT_ENTRY)
                {
                    DBusNative.dbus_message_iter_recurse(dictIter, entryIter);

                    // Get the key
                    IntPtr keyPtr = Marshal.AllocHGlobal(IntPtr.Size);
                    try
                    {
                        DBusNative.dbus_message_iter_get_basic(entryIter, keyPtr);
                        IntPtr keyStrPtr = Marshal.ReadIntPtr(keyPtr);
                        string key = Marshal.PtrToStringAnsi(keyStrPtr);

                        Console.WriteLine($"Found key in response: {key}");

                        if (key == "uri")
                        {
                            // Move to the variant value
                            if (!DBusNative.dbus_message_iter_next(entryIter))
                                continue;

                            // Recurse into variant
                            DBusNative.dbus_message_iter_recurse(entryIter, variantIter);

                            // Get the string value
                            IntPtr uriPtr = Marshal.AllocHGlobal(IntPtr.Size);
                            try
                            {
                                DBusNative.dbus_message_iter_get_basic(variantIter, uriPtr);
                                IntPtr uriStrPtr = Marshal.ReadIntPtr(uriPtr);
                                uri = Marshal.PtrToStringAnsi(uriStrPtr);
                                Console.WriteLine($"Screenshot URI: {uri}");
                                break;
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(uriPtr);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(keyPtr);
                    }

                    if (!DBusNative.dbus_message_iter_next(dictIter))
                        break;
                }

                if (uri == null)
                {
                    Console.WriteLine("No URI found in response");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(variantIter);
            Marshal.FreeHGlobal(entryIter);
            Marshal.FreeHGlobal(dictIter);
            Marshal.FreeHGlobal(iter);
        }

        return uri;
    }

    private string GetErrorMessage(ref DBusNative.DBusError error)
    {
        string errorMessage;

        if (error.message == IntPtr.Zero)
        {
            errorMessage = "Unknown error";
        }
        else
        {
            errorMessage = Marshal.PtrToStringAnsi(error.message) ?? "Unknown error";
        }

        return errorMessage;
    }

    private byte[] ConvertPngToJpeg(byte[] pngData, int quality)
    {
        byte[] jpegData;

        using Image<Rgba32> image = Image.Load<Rgba32>(pngData);
        using MemoryStream ms = new();
        image.Save(ms, new JpegEncoder { Quality = quality });
        jpegData = ms.ToArray();

        return jpegData;
    }

    #endregion
}
