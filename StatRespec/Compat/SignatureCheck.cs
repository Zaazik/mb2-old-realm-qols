using System;
using System.Reflection;

namespace StatRespec.Compat
{
    /// <summary>Pure reflection helper: verify a member exists with the exact expected signature.</summary>
    public static class SignatureCheck
    {
        private const BindingFlags AllInstanceAndStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static bool MethodMatches(Type declaringType, string name, Type returnType, params Type[] paramTypes)
        {
            if (declaringType == null) return false;
            MethodInfo m = declaringType.GetMethod(name, AllInstanceAndStatic, null, paramTypes ?? Type.EmptyTypes, null);
            return m != null && m.ReturnType == returnType;
        }

        public static bool PropertyMatches(Type declaringType, string name, Type propertyType, bool needsSetter)
        {
            if (declaringType == null) return false;
            PropertyInfo p = declaringType.GetProperty(name, AllInstanceAndStatic);
            if (p == null || p.PropertyType != propertyType) return false;
            return !needsSetter || p.GetSetMethod(nonPublic: true) != null;
        }
    }
}
