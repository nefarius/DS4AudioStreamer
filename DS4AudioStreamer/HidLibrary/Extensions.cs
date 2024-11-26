﻿using System.Text;

namespace DS4Windows;

public static class Extensions
{
    public static string ToUtf8String(this byte[] buffer)
    {
        string value = Encoding.UTF8.GetString(buffer);
        return value.Remove(value.IndexOf((char)0));
    }

    public static string ToUtf16String(this byte[] buffer)
    {
        string value = Encoding.Unicode.GetString(buffer);
        return value.Remove(value.IndexOf((char)0));
    }
}