using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MachIntellDrawAI.Infrastructure
{
    /// <summary>
    /// Flattens an exception chain into a readable message. Critical for SolidWorks work,
    /// where the visible error is often a <see cref="TargetInvocationException"/> or a
    /// <see cref="COMException"/> wrapper whose real cause lives in InnerException.
    /// </summary>
    internal static class ExceptionText
    {
        /// <summary>Short message chain for user-facing dialogs (no stack trace).</summary>
        public static string Describe(Exception exception)
        {
            var builder = new StringBuilder();
            var current = Unwrap(exception);
            var depth = 0;
            while (current != null)
            {
                if (depth > 0) builder.Append("\n\nCaused by: ");
                builder.Append(current.Message);
                if (current is COMException com)
                    builder.Append(" (HRESULT 0x").Append(com.ErrorCode.ToString("X8")).Append(')');
                current = current.InnerException;
                depth++;
            }
            return builder.ToString();
        }

        /// <summary>Full detail for the audit log, including type names and stack trace.</summary>
        public static string DescribeVerbose(Exception exception)
        {
            var builder = new StringBuilder();
            var current = exception;
            var depth = 0;
            while (current != null)
            {
                if (depth > 0) builder.Append(" -> INNER: ");
                builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
                if (current is COMException com)
                    builder.Append(" (HRESULT 0x").Append(com.ErrorCode.ToString("X8")).Append(')');
                current = current.InnerException;
                depth++;
            }
            builder.Append(" | STACK: ").Append(exception.StackTrace);
            return builder.ToString();
        }

        /// <summary>Peels a single TargetInvocationException wrapper if it adds no information.</summary>
        private static Exception Unwrap(Exception exception) =>
            exception is TargetInvocationException tie && tie.InnerException != null
                ? tie.InnerException
                : exception;
    }
}
