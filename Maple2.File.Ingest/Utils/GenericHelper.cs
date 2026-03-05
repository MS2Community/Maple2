using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace Maple2.File.Ingest.Utils;
public static class GenericHelper {
    public static void SetValue(PropertyInfo prop, object? obj, object? value) {
        if (obj == null && value == null || value == null) return;
        HandleNonIConvertibleTypes(prop, ref value);
        if (value == null) return;
        if (typeof(IConvertible).IsAssignableFrom(prop.PropertyType)) {
            TryParseObject(prop.PropertyType, value, out object? result);
            prop.SetValue(obj, result);
            return;
        }
        prop.SetValue(obj, value);
    }

    private static object? HandleNonIConvertibleTypes(PropertyInfo prop, ref object? value) {
        if (value == null) return value;
        // Handle TimeSpan type
        if (prop.PropertyType == typeof(TimeSpan)) {
            TimeSpan.TryParse((string) value, CultureInfo.InvariantCulture, out TimeSpan val);
            value = val;
        }
        // Handle array types (int[], short[], etc.)
        if (prop.PropertyType.IsArray) {
            var elementType = prop.PropertyType.GetElementType();
            if (elementType == null) return value;
            string[] segments = ((string) value).Split(',');
            Array destinationArray = Array.CreateInstance(elementType, segments.Length);
            for (int i = 0; i < segments.Length; i++) {
                TryParseObject(elementType, segments[i].Trim(), out object? parseResult);
                destinationArray.SetValue(parseResult ?? default, i);
            }
            value = destinationArray;
        }
        // Handle Vector3 type
        if (prop.PropertyType == typeof(Vector3)) {
            string[] parts = ((string) value).Split(',');
            if (parts.Length != 3) return value;
            float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x);
            float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y);
            float.TryParse(parts[2], CultureInfo.InvariantCulture, out float z);
            value = new Vector3(x, y, z);
        }
        return value;
    }

    private static bool TryParseObject(Type? elementType, object? input, out object? result) {
        if (elementType == null || input == null) {
            result = null;
            return false;
        }

        string inputString = Convert.ToString(input, CultureInfo.InvariantCulture)!;

        // No TryParse method exists for a string, use the result directly.
        if (elementType == typeof(string)) {
            result = inputString;
            return true;
        }

        Type[] argTypes = {
        typeof(string),
        typeof(IFormatProvider),
        elementType.MakeByRefType()
        };

        var method = elementType.GetMethod("TryParse",
            BindingFlags.Public | BindingFlags.Static,
            null, argTypes, null);
        if (method != null) {
            object[] args = [inputString, CultureInfo.InvariantCulture, null!];
            bool success = (bool) method.Invoke(null, args)!;
            result = args[2];
            return success;
        }

        // Fallback without CulturueInfo provided, in case the type does not have a CultureInfo overload.
        Type[] simpleArgs = { typeof(string), elementType.MakeByRefType() };
        method = elementType.GetMethod("TryParse",
            BindingFlags.Public | BindingFlags.Static,
            null, simpleArgs, null);
        if (method != null) {
            object[] args = { inputString, null! };
            bool success = (bool)method.Invoke(null, args)!;
            result = args[1];
            return success;
        }


        result = null;
        return false;
    }
}
