using System;

namespace NickScanWebApp.Mobile.Services.Permissions
{
    /// <summary>
    /// Lightweight wrapper around a permission name to discourage raw string usage.
    /// </summary>
    public readonly record struct PermissionId(string Value)
    {
        public override string ToString() => Value;

        public static implicit operator string(PermissionId id) => id.Value;

        public static PermissionId From(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Permission value cannot be null or whitespace.", nameof(value));
            }

            return new PermissionId(value);
        }
    }
}

