using System;
using System.Linq;
using System.Reflection;

namespace MachIntellDrawAI.Infrastructure
{
    internal static class ComCall
    {
        public static object? Required(object target, string method, params object?[] arguments)
        {
            if (!Try(target, method, arguments, out var result))
                throw new MissingMethodException($"SolidWorks API capability {target.GetType().Name}.{method} is unavailable.");
            return result;
        }

        public static object? Optional(object target, string method, params object?[] arguments)
        {
            Try(target, method, arguments, out var result);
            return result;
        }

        public static bool Try(object target, string method, object?[] arguments, out object? result)
        {
            result = null;
            try
            {
                result = target.GetType().InvokeMember(
                    method,
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                    null,
                    target,
                    arguments);
                return true;
            }
            catch (MissingMethodException) { return false; }
            catch (TargetInvocationException ex) when (ex.InnerException is MissingMethodException) { return false; }
        }

        public static T? Property<T>(object target, params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    var value = target.GetType().InvokeMember(
                        name,
                        BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                        null,
                        target,
                        Array.Empty<object>());
                    if (value == null) return default;
                    if (value is T typed) return typed;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch (Exception ex) when (ex is MissingMethodException || ex is TargetInvocationException || ex is InvalidCastException) { }
            }
            return default;
        }

        public static double? Double(object target, params string[] names)
        {
            foreach (var name in names)
            {
                var value = Property<object>(target, name);
                if (value != null && double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
                    return number;
            }
            return null;
        }
    }
}
