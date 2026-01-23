// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TextureMipmapChecker : EditorWindow
{
    private Vector2 m_scrollPosition;
    private List<Texture2D> m_texturesWithoutMipmaps = new();
    private bool m_showProjectTextures = true;
    private bool m_showSceneTextures = true;
    private string m_currentFolder = "Assets";
    private GUIStyle m_pathLabelStyle;

    [MenuItem("Tools/NorthStar/Texture Mipmap Checker")]
    public static void ShowWindow()
    {
        _ = GetWindow<TextureMipmapChecker>("Mipmap Checker");
    }

    private void OnEnable()
    {
        m_pathLabelStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true
        };
    }

    private void OnGUI()
    {
        GUILayout.Label("Texture Mipmap Checker", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Folder selection using the project browser
        if (GUILayout.Button("Select Folder"))
        {
            var folder = EditorUtility.OpenFolderPanel("Select Folder to Scan", m_currentFolder, "");
            if (!string.IsNullOrEmpty(folder))
            {
                // Convert the full path to a project-relative path
                var dataPath = Application.dataPath;
                if (folder.StartsWith(dataPath))
                {
                    m_currentFolder = "Assets" + folder[dataPath.Length..];
                }
                else
                {
                    _ = EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder within your Unity project.", "OK");
                }
            }
        }

        // Display current folder
        EditorGUILayout.LabelField("Current Folder:", m_currentFolder, m_pathLabelStyle);

        EditorGUILayout.Space();

        m_showProjectTextures = EditorGUILayout.ToggleLeft("Check Project Textures", m_showProjectTextures);
        m_showSceneTextures = EditorGUILayout.ToggleLeft("Check Scene Textures", m_showSceneTextures);

        if (GUILayout.Button("Scan for Textures Without Mipmaps"))
        {
            ScanTextures();
        }

        DisplayResults();
    }

    private bool ShouldIncludeTexture(string path, TextureImporter importer)
    {
        // Skip if it's an editor texture
        if (path.Contains("/Editor/") || path.Contains("/Editor Default Resources/"))
            return false;

        // Skip UI textures
        if (importer.textureType == TextureImporterType.Sprite)
            return false;

        // Skip Unity default textures
        if (path.StartsWith("Packages/com.unity") || path.StartsWith("Library/"))
            return false;

        // Skip textures marked as Editor GUI
        return importer.textureType is not TextureImporterType.SingleChannel and
            not TextureImporterType.Cursor;
    }

    private void DisplayResults()
    {
        if (m_texturesWithoutMipmaps.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Found {m_texturesWithoutMipmaps.Count} textures without mipmaps:");

            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);

            for (var i = 0; i < m_texturesWithoutMipmaps.Count; i++)
            {
                if (m_texturesWithoutMipmaps[i] != null)
                {
                    _ = EditorGUILayout.BeginHorizontal();

                    // Show the texture
                    _ = EditorGUILayout.ObjectField(m_texturesWithoutMipmaps[i], typeof(Texture2D), false);

                    // Show the path
                    var path = AssetDatabase.GetAssetPath(m_texturesWithoutMipmaps[i]);
                    EditorGUILayout.LabelField(path, GUILayout.MaxWidth(300));

                    if (GUILayout.Button("Fix", GUILayout.Width(60)))
                    {
                        EnableMipmaps(m_texturesWithoutMipmaps[i]);
                        i--; // Adjust index since we're removing an item
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Fix All"))
            {
                FixAllTextures();
            }
        }
        else if (m_texturesWithoutMipmaps != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("No textures found without mipmaps!");
        }
    }

    private void ScanTextures()
    {
        m_texturesWithoutMipmaps.Clear();

        // Scan project textures
        if (m_showProjectTextures)
        {
            var guids = AssetDatabase.FindAssets("t:texture2D", new[] { m_currentFolder });

            EditorUtility.DisplayProgressBar("Scanning Textures", "Scanning project textures...", 0f);

            for (var i = 0; i < guids.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Scanning Textures", "Scanning project textures...", (float)i / guids.Length);

                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (importer != null && ShouldIncludeTexture(path, importer))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture != null && !importer.mipmapEnabled)
                    {
                        m_texturesWithoutMipmaps.Add(texture);
                    }
                }
            }
        }

        // Scan scene textures
        if (m_showSceneTextures)
        {
            EditorUtility.DisplayProgressBar("Scanning Textures", "Scanning scene textures...", 0f);

            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (var i = 0; i < renderers.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Scanning Textures", "Scanning scene textures...", (float)i / renderers.Length);

                var materials = renderers[i].sharedMaterials;
                for (var j = 0; j < materials.Length; j++)
                {
                    if (materials[j] != null)
                    {
                        var shader = materials[j].shader;
                        var propertyCount = ShaderUtil.GetPropertyCount(shader);

                        for (var k = 0; k < propertyCount; k++)
                        {
                            if (ShaderUtil.GetPropertyType(shader, k) == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                var propertyName = ShaderUtil.GetPropertyName(shader, k);
                                var texture = materials[j].GetTexture(propertyName) as Texture2D;

                                if (texture != null)
                                {
                                    var path = AssetDatabase.GetAssetPath(texture);
                                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;

                                    if (importer != null &&
                                        ShouldIncludeTexture(path, importer) &&
                                        !importer.mipmapEnabled &&
                                        !ContainsTexture(texture))
                                    {
                                        m_texturesWithoutMipmaps.Add(texture);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        EditorUtility.ClearProgressBar();
    }

    private bool ContainsTexture(Texture2D texture)
    {
        for (var i = 0; i < m_texturesWithoutMipmaps.Count; i++)
        {
            if (m_texturesWithoutMipmaps[i] == texture)
            {
                return true;
            }
        }
        return false;
    }

    private void EnableMipmaps(Texture2D texture)
    {
        var path = AssetDatabase.GetAssetPath(texture);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer != null)
        {
            importer.mipmapEnabled = true;
            AssetDatabase.ImportAsset(path);
            _ = m_texturesWithoutMipmaps.Remove(texture);
        }
    }

    private void FixAllTextures()
    {
        var texturesToFix = new List<Texture2D>(m_texturesWithoutMipmaps);
        for (var i = 0; i < texturesToFix.Count; i++)
        {
            EnableMipmaps(texturesToFix[i]);
        }
    }
}