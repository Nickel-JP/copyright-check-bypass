#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

namespace Nogut.VRCCopyrightAgreementAutoAccept
{
    [InitializeOnLoad]
    internal static class VRCCopyrightAgreementAutoAccept
    {
        private const string HarmonyId = "com.nogut.vrchat-copyright-agreement-auto-accept";
        private const string LogPrefix = "[Copyright Agreement Auto Accept] ";
        private const BindingFlags AllMembers = BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance |
            BindingFlags.FlattenHierarchy;

        private static object _harmony;
        private static MethodInfo _unpatchAllMethod;
        private static SdkBindings _bindings;

        static VRCCopyrightAgreementAutoAccept()
        {
            EditorApplication.delayCall += InstallPatch;
        }

        [MenuItem("Tools/VRChat SDK/Copyright Agreement Auto Accept/Reinstall Patch")]
        private static void ReinstallPatch()
        {
            if (RemovePatch())
            {
                InstallPatch();
            }
        }

        public static void InstallPatch()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                var agreementType = FindLoadedType("VRC.SDKBase.VRCCopyrightAgreement");
                var harmonyType = FindLoadedType("HarmonyLib.Harmony");
                if (agreementType == null || harmonyType == null)
                {
                    Debug.LogWarning(LogPrefix +
                        "VRChat SDK または Harmony が読み込まれていないため、" +
                        "自動同意を有効にしません。SDK 標準の動作を維持します。");
                    return;
                }

                var bindings = new SdkBindings(agreementType);
                var harmonyMethodType = harmonyType.Assembly.GetType(
                    "HarmonyLib.HarmonyMethod", true);
                var patchMethod = harmonyType.GetMethod(
                    "Patch", BindingFlags.Public | BindingFlags.Instance, null,
                    new[]
                    {
                        typeof(MethodBase), harmonyMethodType, harmonyMethodType,
                        harmonyMethodType, harmonyMethodType
                    }, null);
                var unpatchAllMethod = harmonyType.GetMethod(
                    "UnpatchAll", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(string) }, null);
                var prefixMethod = typeof(VRCCopyrightAgreementAutoAccept).GetMethod(
                    nameof(CheckCopyrightAgreementPrefix), AllMembers);

                if (patchMethod == null || unpatchAllMethod == null || prefixMethod == null)
                {
                    throw new MissingMethodException(
                        "Harmony の Patch/UnpatchAll またはパッチ用メソッドが見つかりません。");
                }

                var prefix = Activator.CreateInstance(
                    harmonyMethodType, new object[] { prefixMethod });
                _harmony = Activator.CreateInstance(
                    harmonyType, new object[] { HarmonyId });
                _unpatchAllMethod = unpatchAllMethod;
                _bindings = bindings;
                patchMethod.Invoke(_harmony, new object[]
                {
                    bindings.TargetMethod, prefix, null, null, null
                });

                Debug.Log(
                    LogPrefix + "著作権同意モーダルを" +
                    "自動同意に置き換えました。");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(LogPrefix +
                    "自動同意を有効にできませんでした。SDK/Harmony の互換性を確認してください。\n" +
                    exception.Message);
                Debug.LogException(exception);
                RemovePatch();
            }
        }

        private static bool RemovePatch()
        {
            _bindings = null;
            if (_harmony == null)
            {
                return true;
            }

            try
            {
                _unpatchAllMethod.Invoke(_harmony, new object[] { HarmonyId });
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }

            _harmony = null;
            _unpatchAllMethod = null;
            return true;
        }

        private static bool CheckCopyrightAgreementPrefix(
            object __0,
            object __1,
            ref Task __result)
        {
            var bindings = _bindings;
            if (bindings == null)
            {
                return true;
            }

            __result = AcceptAgreementAsync(bindings, __0, __1);
            return false;
        }

        private static async Task AcceptAgreementAsync(
            SdkBindings bindings,
            object pipelineManager,
            object content)
        {
            if (pipelineManager == null ||
                (pipelineManager is UnityEngine.Object unityObject && unityObject == null))
            {
                throw new ArgumentNullException(nameof(pipelineManager));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var blueprintId = bindings.GetBlueprintId(pipelineManager);
            if (string.IsNullOrWhiteSpace(blueprintId))
            {
                throw new Exception("No blueprint ID set");
            }

            var contentId = bindings.GetContentId(content);
            if (string.IsNullOrWhiteSpace(contentId))
            {
                contentId = blueprintId;
            }

            while (true)
            {
                var userId = bindings.GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw bindings.CreateOwnershipException(
                        "Current user is not logged in");
                }

                var agreementKey = userId + ":" + contentId;

                if (bindings.AcceptedAgreementKeys.Contains(agreementKey))
                {
                    if (bindings.IsCurrentUser(userId))
                    {
                        return;
                    }

                    continue;
                }

                if (bindings.PendingAgreements.TryGetValue(
                        agreementKey,
                        out var pendingAgreement))
                {
                    if (await pendingAgreement)
                    {
                        if (bindings.IsCurrentUser(userId))
                        {
                            return;
                        }

                        continue;
                    }

                    if (bindings.IsCurrentUser(userId))
                    {
                        throw bindings.CreateOwnershipException(
                            "Failed to agree to the ownership agreement");
                    }

                    continue;
                }

                Debug.Log(
                    "[Copyright Agreement Auto Accept] 同意 API を送信します。" +
                    $" user={userId}, content={contentId}");

                var agreementTask = AgreeAsync(bindings, contentId);
                bindings.PendingAgreements[agreementKey] = agreementTask;

                try
                {
                    if (!await agreementTask)
                    {
                        if (bindings.IsCurrentUser(userId))
                        {
                            throw bindings.CreateOwnershipException(
                                "Failed to agree to the ownership agreement");
                        }

                        continue;
                    }

                    bindings.AcceptedAgreementKeys.Add(agreementKey);
                }
                finally
                {
                    bindings.PendingAgreements.Remove(agreementKey);
                }

                if (bindings.IsCurrentUser(userId))
                {
                    return;
                }

                Debug.Log(
                    "[Copyright Agreement Auto Accept] ユーザー切替を検出したため、" +
                    "新しいアカウントで同意 API を再送します。");
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Type RequireLoadedType(string fullName)
        {
            return FindLoadedType(fullName) ?? throw new TypeLoadException(
                "SDK の型が見つかりません: " + fullName);
        }

        private static MethodInfo RequireStaticMethod(
            Type type, string name, Type returnType, params Type[] parameterTypes)
        {
            var method = type.GetMethod(name, AllMembers, null, parameterTypes, null);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }

            if (!method.IsStatic || method.ContainsGenericParameters ||
                method.ReturnType != returnType)
            {
                throw new InvalidOperationException(
                    $"{type.FullName}.{name} のシグネチャが非対応です。" +
                    $"期待値: static {returnType}、実際: " +
                    $"{(method.IsStatic ? "static " : "instance ")}{method.ReturnType}");
            }

            return method;
        }

        private static PropertyInfo RequireProperty(
            Type type, string name, Type propertyType, bool isStatic)
        {
            var property = type.GetProperty(name, AllMembers);
            var getter = property?.GetGetMethod(true);
            if (getter == null || getter.IsStatic != isStatic ||
                property.PropertyType != propertyType ||
                property.GetIndexParameters().Length != 0)
            {
                throw new InvalidOperationException(
                    $"{type.FullName}.{name} は読み取り可能な " +
                    $"{propertyType} の{(isStatic ? "static" : "instance")}プロパティである必要があります。");
            }

            return property;
        }

        private static async Task<bool> AgreeAsync(SdkBindings bindings, string contentId)
        {
            object result;
            try
            {
                result = bindings.AgreeMethod.Invoke(null, new object[] { contentId });
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    "SDK の Agree(string) の呼び出しに失敗しました。",
                    exception.InnerException ?? exception);
            }

            if (!(result is Task<bool> agreementTask))
            {
                throw new InvalidOperationException(
                    "SDK の Agree(string) が Task<bool> を返しませんでした。");
            }

            return await agreementTask;
        }

        private sealed class SdkBindings
        {
            private readonly FieldInfo _blueprintIdField;
            private readonly PropertyInfo _contentIdProperty;
            private readonly PropertyInfo _currentUserProperty;
            private readonly PropertyInfo _userIdProperty;
            private readonly ConstructorInfo _ownershipExceptionConstructor;

            public MethodInfo TargetMethod { get; }
            public MethodInfo AgreeMethod { get; }

            public HashSet<string> AcceptedAgreementKeys { get; } = new HashSet<string>();
            public Dictionary<string, Task<bool>> PendingAgreements { get; } =
                new Dictionary<string, Task<bool>>();

            public SdkBindings(Type agreementType)
            {
                var pipelineType = RequireLoadedType("VRC.Core.PipelineManager");
                var contentType = RequireLoadedType("VRC.SDKBase.Editor.Api.IVRCContent");
                var userType = RequireLoadedType("VRC.Core.APIUser");
                var ownershipExceptionType = RequireLoadedType(
                    "VRC.SDKBase.Editor.OwnershipException");

                TargetMethod = RequireStaticMethod(agreementType,
                    "CheckCopyrightAgreement", typeof(Task), pipelineType, contentType);
                AgreeMethod = RequireStaticMethod(agreementType,
                    "Agree", typeof(Task<bool>), typeof(string));
                _blueprintIdField = pipelineType.GetField("blueprintId", AllMembers);
                if (_blueprintIdField == null || _blueprintIdField.IsStatic ||
                    _blueprintIdField.FieldType != typeof(string))
                {
                    throw new InvalidOperationException(
                        "PipelineManager.blueprintId は string の instance フィールドである必要があります。");
                }

                _contentIdProperty = RequireProperty(contentType, "ID", typeof(string), false);
                _currentUserProperty = RequireProperty(userType, "CurrentUser", userType, true);
                _userIdProperty = RequireProperty(userType, "id", typeof(string), false);
                _ownershipExceptionConstructor = ownershipExceptionType.GetConstructor(
                    new[] { typeof(string) });
                if (!typeof(Exception).IsAssignableFrom(ownershipExceptionType) ||
                    _ownershipExceptionConstructor == null)
                {
                    throw new InvalidOperationException(
                        "SDK の OwnershipException(string) が見つかりません。");
                }
            }

            public string GetBlueprintId(object pipelineManager)
            {
                return (string)_blueprintIdField.GetValue(pipelineManager);
            }

            public string GetContentId(object content)
            {
                return (string)_contentIdProperty.GetValue(content);
            }

            public string GetCurrentUserId()
            {
                var user = _currentUserProperty.GetValue(null);
                return user == null ? null : (string)_userIdProperty.GetValue(user);
            }

            public bool IsCurrentUser(string userId)
            {
                return GetCurrentUserId() == userId;
            }

            public Exception CreateOwnershipException(string message)
            {
                return (Exception)_ownershipExceptionConstructor.Invoke(new object[] { message });
            }
        }
    }
}
#endif
