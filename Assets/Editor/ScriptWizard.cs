using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.CodeDom.Compiler;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class ScriptWizard : EditorWindow
{
    [Serializable]
    public class Variable : IComparable
    {
        public string name;
        public Object value;
        public UsableType usableType;

        int m_TypeIndex;

        public Variable(string name, UsableType usableType)
        {
            this.name = name;
            this.usableType = usableType;
        }

        public bool GUI(UsableType[] usableTypes)
        {
            bool removeThis = false;
            EditorGUILayout.BeginHorizontal();
            name = EditorGUILayout.TextField(name);
            m_TypeIndex = EditorGUILayout.Popup(m_TypeIndex, UsableType.GetNamewithSortingArray(usableTypes));
            usableType = usableTypes[m_TypeIndex];
            value = EditorGUILayout.ObjectField(value, usableType.type, true);
            if (GUILayout.Button("Remove", GUILayout.Width(60f)))
            {
                removeThis = true;
            }
            EditorGUILayout.EndHorizontal();

            return removeThis;
        }

        public int CompareTo(object obj)
        {
            if (obj == null)
                return 1;

            UsableType other = (UsableType)obj;

            if (other == null)
                throw new ArgumentException("This object is not a Variable.");

            return name.ToLower().CompareTo(other.name.ToLower());
        }

        public static UsableType[] GetUsableTypesFromVariableArray(Variable[] variables)
        {
            UsableType[] usableTypes = new UsableType[variables.Length];
            for (int i = 0; i < usableTypes.Length; i++)
            {
                usableTypes[i] = variables[i].usableType;
            }
            return usableTypes;
        }
    }


    public class UsableType : IComparable
    {
        public readonly string name;
        public readonly string nameWithSorting;
        public readonly string additionalNamespace;
        public readonly GUIContent guiContentWithSorting;
        public readonly Type type;

        public readonly string[] unrequiredNamespaces =
        {
            "UnityEngine",
        };
        public const string blankAdditionalNamespace = "";

        const string k_NameForNullType = "None";

        public UsableType(Type usableType)
        {
            type = usableType;

            if (type != null)
            {
                name = usableType.Name;
                nameWithSorting = name.ToUpper()[0] + "/" + name;
                additionalNamespace = unrequiredNamespaces.All(t => usableType.Namespace != t) ? usableType.Namespace : blankAdditionalNamespace;
            }
            else
            {
                name = k_NameForNullType;
                nameWithSorting = k_NameForNullType;
                additionalNamespace = blankAdditionalNamespace;
            }

            guiContentWithSorting = new GUIContent(nameWithSorting);
        }

        public UsableType(string name)
        {
            this.name = name;
            nameWithSorting = name.ToUpper()[0] + "/" + name;
            additionalNamespace = blankAdditionalNamespace;
            guiContentWithSorting = new GUIContent(nameWithSorting);
        }

        public int CompareTo(object obj)
        {
            if (obj == null)
                return 1;

            UsableType other = (UsableType)obj;

            if (other == null)
                throw new ArgumentException("This object is not a UsableType.");

            return name.ToLower().CompareTo(other.name.ToLower());
        }

        public static UsableType[] GetUsableTypeArray(Type[] types, params UsableType[] additionalUsableTypes)
        {
            List<UsableType> usableTypeList = new List<UsableType>();
            for (int i = 0; i < types.Length; i++)
            {
                usableTypeList.Add(new UsableType(types[i]));
            }
            usableTypeList.AddRange(additionalUsableTypes);
            return usableTypeList.ToArray();
        }

        public static UsableType[] AmalgamateUsableTypes(UsableType[] usableTypeArray, params UsableType[] usableTypes)
        {
            List<UsableType> usableTypeList = new List<UsableType>();
            for (int i = 0; i < usableTypes.Length; i++)
            {
                usableTypeList.Add(usableTypes[i]);
            }
            usableTypeList.AddRange(usableTypeArray);
            return usableTypeList.ToArray();
        }

        public static string[] GetNamewithSortingArray(UsableType[] usableTypes)
        {
            if (usableTypes == null || usableTypes.Length == 0)
                return new string[0];

            string[] displayNames = new string[usableTypes.Length];
            for (int i = 0; i < displayNames.Length; i++)
            {
                displayNames[i] = usableTypes[i].nameWithSorting;
            }
            return displayNames;
        }

        public static GUIContent[] GetGUIContentWithSortingArray(UsableType[] usableTypes)
        {
            if (usableTypes == null || usableTypes.Length == 0)
                return new GUIContent[0];

            GUIContent[] guiContents = new GUIContent[usableTypes.Length];
            for (int i = 0; i < guiContents.Length; i++)
            {
                guiContents[i] = usableTypes[i].guiContentWithSorting;
            }
            return guiContents;
        }

        public static string[] GetDistinctAdditionalNamespaces(UsableType[] usableTypes)
        {
            if (usableTypes == null || usableTypes.Length == 0)
                return new string[0];

            string[] namespaceArray = new string[usableTypes.Length];
            for (int i = 0; i < namespaceArray.Length; i++)
            {
                namespaceArray[i] = usableTypes[i].additionalNamespace;
            }
            return namespaceArray.Distinct().ToArray();
        }
    }

    public enum CreationError
    {
        NoError,
        MonoScriptAssetAlreadyExists,
    }


    public string scriptName = "";
    [SerializeField] List<Variable> exposedReferences = new List<Variable>();
    bool m_CreateButtonPressed;
    Vector2 m_ScrollViewPos;
    CreationError m_CreationError;

    readonly GUIContent m_ScriptNameContent = new GUIContent("Script Name", "This is the name that will represent the script.  E.G. AwesomeBehaviour");
    readonly GUIContent m_CreatedAtContent = new GUIContent("Create At", "the script is created under the Assets folder at Default.");
    readonly GUIContent m_ExposedReferencesContent = new GUIContent("Exposed References", "Exposed References are references to objects in a scene that your Script needs. For example, if you want to tween between two Transforms, they will need to be Exposed References.");

    const string k_Tab = "    ";
    const int k_ScriptNameCharLimit = 64;
    const float k_WindowWidth = 500f;
    const float k_MaxWindowHeight = 800f;
    const float k_ScreenSizeWindowBuffer = 50f;

    static UsableType[] s_ExposedReferenceTypes;

    Object createAssetPath;
    bool createAndAddComponent;
    GameObject addToTarget;
    bool isLinkObject;
    bool useSerializeFieldAttribute;

    [MenuItem("Window/Script Wizard...")]
    static void CreateWindow()
    {
        ScriptWizard wizard = GetWindow<ScriptWizard>(true, "Script Wizard", true);

        Vector2 position = Vector2.zero;
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
            position = new Vector2(sceneView.position.x, sceneView.position.y);
        wizard.position = new Rect(position.x + k_ScreenSizeWindowBuffer, position.y + k_ScreenSizeWindowBuffer, k_WindowWidth, Mathf.Min(Screen.currentResolution.height - k_ScreenSizeWindowBuffer, k_MaxWindowHeight));

        wizard.Show();

        Init();
    }

    static void Init()
    {
        Type[] componentTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).Where(t => typeof(Component).IsAssignableFrom(t)).Where(t => t.IsPublic).ToArray();

        UsableType gameObjectUsableType = new UsableType(typeof(GameObject));
        UsableType[] defaultUsableTypes = UsableType.GetUsableTypeArray(componentTypes, gameObjectUsableType);

        List<UsableType> exposedRefTypeList = defaultUsableTypes.ToList();
        exposedRefTypeList.Sort();
        s_ExposedReferenceTypes = exposedRefTypeList.ToArray();
    }

    void OnGUI()
    {
        if (s_ExposedReferenceTypes == null)
            Init();

        if (s_ExposedReferenceTypes == null)
        {
            EditorGUILayout.HelpBox("Failed to initialise.", MessageType.Error);
            return;
        }

        if (EditorApplication.isCompiling)
        {
            EditorGUILayout.HelpBox("Compiling...", MessageType.Info);
            return;
        }

        m_ScrollViewPos = EditorGUILayout.BeginScrollView(m_ScrollViewPos);

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.BeginVertical(GUI.skin.box);
        scriptName = EditorGUILayout.TextField(m_ScriptNameContent, scriptName);
        createAssetPath = EditorGUILayout.ObjectField(m_CreatedAtContent, createAssetPath, typeof(Object), false);

        bool scriptNameNotEmpty = !string.IsNullOrEmpty(scriptName);
        bool scriptNameFormatted = CodeGenerator.IsValidLanguageIndependentIdentifier(scriptName);
        if (!scriptNameNotEmpty || !scriptNameFormatted)
        {
            EditorGUILayout.HelpBox("The Script needs a name which starts with a capital letter and contains no spaces or special characters.", MessageType.Error);
        }
        bool scriptNameTooLong = scriptName.Length > k_ScriptNameCharLimit;
        if (scriptNameTooLong)
        {
            EditorGUILayout.HelpBox("The Script needs a name which is fewer than " + k_ScriptNameCharLimit + " characters long.", MessageType.Error);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        bool exposedVariablesNamesValid = VariableListGUI(exposedReferences, s_ExposedReferenceTypes, m_ExposedReferencesContent, "newExposedReference");
        bool allUniqueVariableNames = AllVariablesUniquelyNamed();
        if (!allUniqueVariableNames)
        {
            EditorGUILayout.HelpBox("All variable names need unique.", MessageType.Error);
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        if (scriptNameNotEmpty && scriptNameFormatted && allUniqueVariableNames && exposedVariablesNamesValid && !scriptNameTooLong)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            useSerializeFieldAttribute = EditorGUILayout.Toggle("Use [SerializeField]", useSerializeFieldAttribute);
            createAndAddComponent = EditorGUILayout.Toggle("Create And AddComponent", createAndAddComponent);
            if (createAndAddComponent)
            {
                addToTarget = (GameObject)EditorGUILayout.ObjectField("AddComponentTo", addToTarget, typeof(GameObject), true);
            }

            if (GUILayout.Button("Create", GUILayout.Width(60f)))
            {
                m_CreateButtonPressed = true;
                m_CreationError = CreateScripts();

                if (m_CreationError == CreationError.NoError)
                {
                    if (createAndAddComponent)
                    {
                        isLinkObject = true;
                    }
                    else
                    {
                        Close();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        if (m_CreateButtonPressed)
        {
            switch (m_CreationError)
            {
                case CreationError.NoError:
                    EditorGUILayout.HelpBox("Script was successfully created.", MessageType.Info);
                    break;
                case CreationError.MonoScriptAssetAlreadyExists:
                    EditorGUILayout.HelpBox("The type " + scriptName + " already exists, no files were created.", MessageType.Error);
                    break;
            }
        }

        if (GUILayout.Button("Reset", GUILayout.Width(60f)))
        {
            ResetWindow();
        }

        EditorGUILayout.EndScrollView();
    }

    void Update()
    {
        // コンパイルおわるまで待って、終わったらFieldに値をセット
        if (isLinkObject && !EditorApplication.isCompiling)
        {
            Debug.Log("Compile Finished.");
            isLinkObject = false;
            Init();
            LinkExposedReference();
            Close();
        }
    }

    void LinkExposedReference()
    {
        Type createdType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.Name == scriptName);
        var script = addToTarget.AddComponent(createdType);

        BindingFlags flags = useSerializeFieldAttribute ?
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy :
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        foreach (var reference in exposedReferences)
        {
            if (reference.value == null) continue;
            createdType
                .GetFields(flags)
                .First(x => x.Name == reference.name)
                .SetValue(script, reference.value);
            Debug.Log("Link Exposed Reference named: " + reference.name + " and type: " + reference.value.GetType().Name + " to added Component.");
        }
        AssetDatabase.SaveAssets();
    }

    bool VariableListGUI(List<Variable> variables, UsableType[] usableTypes, GUIContent guiContent, string newName)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);

        EditorGUILayout.LabelField(guiContent);

        int indexToRemove = -1;
        bool allNamesValid = true;
        for (int i = 0; i < variables.Count; i++)
        {
            if (variables[i].GUI(usableTypes))
                indexToRemove = i;

            if (!CodeGenerator.IsValidLanguageIndependentIdentifier(variables[i].name))
            {
                allNamesValid = false;
            }
        }

        if (indexToRemove != -1)
            variables.RemoveAt(indexToRemove);

        if (GUILayout.Button("Add", GUILayout.Width(40f)))
            variables.Add(new Variable(newName, usableTypes[0]));

        if (!allNamesValid)
            EditorGUILayout.HelpBox("One of the variables has an invalid character, make sure they don't contain any spaces or special characters.", MessageType.Error);

        EditorGUILayout.EndVertical();

        return allNamesValid;
    }

    bool AllVariablesUniquelyNamed()
    {
        for (int i = 0; i < exposedReferences.Count; i++)
        {
            string exposedRefName = exposedReferences[i].name;

            for (int j = 0; j < exposedReferences.Count; j++)
            {
                if (i != j && exposedRefName == exposedReferences[j].name)
                    return false;
            }
        }
        return true;
    }

    CreationError CreateScripts()
    {
        if (ScriptAlreadyExists(scriptName))
            return CreationError.MonoScriptAssetAlreadyExists;

        CreateScript(scriptName, MakeBehaviourText());

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return CreationError.NoError;
    }

    static bool ScriptAlreadyExists(string scriptName)
    {
        string[] guids = AssetDatabase.FindAssets(scriptName);

        if (guids.Length == 0)
            return false;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (assetType == typeof(MonoScript))
                return true;
        }

        return false;
    }

    void CreateScript(string fileName, string content)
    {
        string path = "";
        if (createAssetPath != null && AssetDatabase.GetAssetPath(createAssetPath) != "Assets")
        {
            path = Application.dataPath + "/" + AssetDatabase.GetAssetPath(createAssetPath).Replace("Assets/", "") + "/" + fileName + ".cs";
        }
        else
        {
            path = Application.dataPath + "/" + fileName + ".cs";
        }

        // string path = Application.dataPath + "/" + assetPath + "/" + fileName + ".cs";
        using (StreamWriter writer = File.CreateText(path))
            writer.Write(content);
    }

    void ResetWindow()
    {
        scriptName = "";
        exposedReferences = new List<Variable>();
    }

    string MakeBehaviourText()
    {
        return
            "using System;\n" +
            "using System.Collections;\n" +
            "using System.Collections.Generic;\n" +
            "using UnityEngine;\n" +
            AdditionalNamespacesToString() +
            "\n" +
            "public class " + scriptName + " : MonoBehaviour\n" +
            "{\n" +
            ExposedReferencesAsScriptVariablesToString() +
            // BehaviourVariablesToString() +
            "}\n";
    }

    string AdditionalNamespacesToString()
    {
        UsableType[] exposedReferenceTypes = Variable.GetUsableTypesFromVariableArray(exposedReferences.ToArray());
        UsableType[] allUsedTypes = new UsableType[exposedReferenceTypes.Length /*+ behaviourVariableTypes.Length */];
        for (int i = 0; i < exposedReferenceTypes.Length; i++)
        {
            allUsedTypes[i] = exposedReferenceTypes[i];
        }

        string[] distinctNamespaces = UsableType.GetDistinctAdditionalNamespaces(allUsedTypes).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        string returnVal = "";
        for (int i = 0; i < distinctNamespaces.Length; i++)
        {
            returnVal += "using " + distinctNamespaces[i] + ";\n";
        }
        return returnVal;
    }

    string ExposedReferencesAsScriptVariablesToString()
    {
        string returnVal = "";
        for (int i = 0; i < exposedReferences.Count; i++)
        {
            if (useSerializeFieldAttribute)
            {
                returnVal += k_Tab + "[SerializeField] " + exposedReferences[i].usableType.name + " " + exposedReferences[i].name + ";\n";
            }
            else
            {
                returnVal += k_Tab + "public " + exposedReferences[i].usableType.name + " " + exposedReferences[i].name + ";\n";
            }
        }
        return returnVal;
    }
}
