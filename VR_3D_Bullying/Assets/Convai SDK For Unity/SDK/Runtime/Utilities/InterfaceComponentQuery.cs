using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Convai.Runtime.Utilities
{
    /// <summary>
    ///     Finds scene components by interface without scanning all MonoBehaviours.
    /// </summary>
    internal static class InterfaceComponentQuery
    {
        private static readonly object CacheLock = new();
        private static readonly Dictionary<Type, Type[]> ImplementerCache = new();

        public static IReadOnlyList<TInterface> FindObjects<TInterface>(
            FindObjectsInactive includeInactive = FindObjectsInactive.Exclude) where TInterface : class
        {
            Type targetType = typeof(TInterface);
            Type[] implementerTypes = GetImplementerTypes(targetType);
            if (implementerTypes.Length == 0) return Array.Empty<TInterface>();

            List<TInterface> results = new();
            HashSet<int> seenInstanceIds = new();

            foreach (Type implementerType in implementerTypes)
            {
                Object[] foundObjects = Object.FindObjectsByType(
                    implementerType,
                    includeInactive,
                    FindObjectsSortMode.None);

                for (int i = 0; i < foundObjects.Length; i++)
                {
                    Object foundObject = foundObjects[i];
                    if (foundObject is TInterface instance && seenInstanceIds.Add(foundObject.GetInstanceID()))
                        results.Add(instance);
                }
            }

            return results;
        }

        public static bool TryFindFirst<TInterface>(
            out TInterface result,
            FindObjectsInactive includeInactive = FindObjectsInactive.Exclude) where TInterface : class
        {
            IReadOnlyList<TInterface> objects = FindObjects<TInterface>(includeInactive);
            if (objects.Count > 0)
            {
                result = objects[0];
                return true;
            }

            result = null;
            return false;
        }

        private static Type[] GetImplementerTypes(Type targetType)
        {
            lock (CacheLock)
            {
                if (ImplementerCache.TryGetValue(targetType, out Type[] cachedTypes)) return cachedTypes;

                List<Type> implementerTypes = new();
                HashSet<Type> seenTypes = new();

#if UNITY_EDITOR
                foreach (Type candidate in TypeCache.GetTypesDerivedFrom(targetType))
                {
                    if (!IsValidImplementerType(candidate, targetType) ||
                        !seenTypes.Add(candidate))
                        continue;

                    implementerTypes.Add(candidate);
                }
#else
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!IsRuntimeAssembly(assembly))
                    {
                        continue;
                    }

                    Type[] assemblyTypes;
                    try
                    {
                        assemblyTypes = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        assemblyTypes = ex.Types;
                    }

                    if (assemblyTypes == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < assemblyTypes.Length; i++)
                    {
                        Type candidate = assemblyTypes[i];
                        if (candidate == null ||
                            candidate.IsAbstract ||
                            candidate.IsInterface ||
                            candidate.IsGenericTypeDefinition)
                        {
                            continue;
                        }

                        if (!IsValidImplementerType(candidate, targetType) ||
                            !seenTypes.Add(candidate))
                        {
                            continue;
                        }

                        implementerTypes.Add(candidate);
                    }
                }
#endif

                Type[] resolvedTypes = implementerTypes.ToArray();
                ImplementerCache[targetType] = resolvedTypes;
                return resolvedTypes;
            }
        }

        private static bool IsValidImplementerType(Type candidate, Type targetType)
        {
            if (candidate == null ||
                candidate.IsAbstract ||
                candidate.IsInterface ||
                candidate.IsGenericTypeDefinition)
                return false;

            if (!typeof(MonoBehaviour).IsAssignableFrom(candidate)) return false;

            if (!targetType.IsAssignableFrom(candidate)) return false;

            return IsRuntimeAssembly(candidate.Assembly);
        }

        private static bool IsRuntimeAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic) return false;

            string assemblyName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName)) return false;

            if (assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                assemblyName.EndsWith(".Editor", StringComparison.Ordinal) ||
                assemblyName.Contains(".Editor.", StringComparison.Ordinal) ||
                assemblyName.StartsWith("nunit.framework", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("UnityEngine.TestRunner", StringComparison.Ordinal) ||
                assemblyName.StartsWith("UnityEditor.TestRunner", StringComparison.Ordinal))
                return false;

            return true;
        }
    }
}
