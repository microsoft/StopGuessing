using System;
using System.Linq;
using System.Reflection;

namespace StopGuessing.DataStructures
{
    internal class StaticUtilities
    {
        //// From: http://stackoverflow.com/questions/74616/how-to-detect-if-type-is-another-generic-type/1075059#1075059
        //public static bool IsAssignableToGenericType(Type givenType, Type genericType)
        //{
        //    if (givenType == null || genericType == null)
        //        return false;

        //    // Check all interfaces (which will include any ancestor interfaces) to see if they match the genericType
        //    if (givenType.GetInterfaces().Any(it => it.IsConstructedGenericType && it.GetGenericTypeDefinition() == genericType))
        //        return true;

        //    // Walk up the ancestry chain to all types this type inherits from looking for the type match
        //    for (; givenType != null; givenType = givenType.BaseType)
        //    {
        //        if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
        //            return true;
        //    }

        //    return false;
        //}
    }
}
