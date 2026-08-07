using System;
using System.IO;
using KupoUI.PR.Textures;

namespace KupoUI.PR
{
    internal static class ModulePathFilter
    {
        /// <summary>
        /// Determines whether a configuration file (.json) under <paramref name="modulesRootPath"/>
        /// should be skipped based on active pack selections for 
        /// 01-UI-Themes, 02-UI-Frames, 03-UI-BgColor, 04-UI-Cursors, and 05-Button-Prompts,
        /// as well as block folders and game tag filtering (FF1-FF6).
        /// </summary>
        public static bool ShouldSkipConfigFile(string filePath, string modulesRootPath)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(modulesRootPath))
            {
                return true;
            }

            var normalizedFile = filePath.Replace('\\', '/');
            var normalizedRoot = modulesRootPath.Replace('\\', '/').TrimEnd('/');

            if (!normalizedFile.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relativePath = normalizedFile.Substring(normalizedRoot.Length + 1);
            var pathSegments = relativePath.Split('/');

            if (pathSegments.Length == 0)
            {
                return false;
            }

            // 1. Category Folder Filtering:
            // For folders 01-UI-Themes, 02-UI-Frames, 03-UI-BgColor, 04-UI-Cursors, 05-Button-Prompts:
            // ONLY load .json files from the configured active pack folder.
            var category = pathSegments[0];

            if (category.Equals("01-UI-Themes", StringComparison.OrdinalIgnoreCase))
            {
                if (pathSegments.Length < 2 || !pathSegments[1].Equals(TextureResolver.UiThemesPack, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (category.Equals("02-UI-Frames", StringComparison.OrdinalIgnoreCase))
            {
                if (pathSegments.Length < 2 || !pathSegments[1].Equals(TextureResolver.UiFramesPack, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (category.Equals("03-UI-BgColor", StringComparison.OrdinalIgnoreCase))
            {
                if (pathSegments.Length < 2 || !pathSegments[1].Equals(TextureResolver.UiBgColorPack, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (category.Equals("04-UI-Cursors", StringComparison.OrdinalIgnoreCase))
            {
                if (pathSegments.Length < 2 || !pathSegments[1].Equals(TextureResolver.CursorsPack, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (category.Equals("05-Button-Prompts", StringComparison.OrdinalIgnoreCase))
            {
                if (pathSegments.Length < 2 || !pathSegments[1].Equals(TextureResolver.ButtonPromptsPack, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 2. Block and GameTag filtering across all segments in the path:
            var gameTag = TextureResolver.CurrentGameTag;
            foreach (var segment in pathSegments)
            {
                if (segment.StartsWith("block", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var isGameTagFolder =
                    segment.Equals("FF1", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("FF2", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("FF3", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("FF4", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("FF5", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("FF6", StringComparison.OrdinalIgnoreCase);

                if (isGameTagFolder && !segment.Equals(gameTag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
