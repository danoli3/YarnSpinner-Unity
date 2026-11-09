/*
Yarn Spinner is licensed to you under the terms found in the file LICENSE.md.
*/

namespace Yarn.Unity.Editor
{
    using System.IO;
    using UnityEditor;
    using UnityEngine;

#nullable enable

    /// <summary>Re-imports Yarn Projects when the project-wide codec settings (or the default codec asset) change.</summary>
    [InitializeOnLoad]
    static class StringTableCodecDependency
    {
        public const string Name = "YarnSpinner.StringTableCodec";

        static StringTableCodecDependency() => Register();

        internal static void Register()
        {
            var settings = YarnSpinnerProjectSettings.GetOrCreateSettings();
            var hash = new Hash128();
            hash.Append(settings.encryptByDefault ? 1 : 0);
            hash.Append(settings.defaultCodecGuid ?? string.Empty);
            hash.Append(settings.defaultSalt ?? string.Empty);

            // Hash the saved file, not the in-memory object, so every import process sees what's on disk.
            var path = string.IsNullOrEmpty(settings.defaultCodecGuid) ? string.Empty : AssetDatabase.GUIDToAssetPath(settings.defaultCodecGuid);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) { hash.Append(File.ReadAllText(path)); }

            AssetDatabase.RegisterCustomDependency(Name, hash);
        }
    }

    class StringTableCodecDependencyPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var settings = YarnSpinnerProjectSettings.GetOrCreateSettings();
            var path = string.IsNullOrEmpty(settings.defaultCodecGuid) ? null : AssetDatabase.GUIDToAssetPath(settings.defaultCodecGuid);
            if (!string.IsNullOrEmpty(path) && System.Array.IndexOf(imported, path) >= 0)
            {
                StringTableCodecDependency.Register();
            }
        }
    }
}
