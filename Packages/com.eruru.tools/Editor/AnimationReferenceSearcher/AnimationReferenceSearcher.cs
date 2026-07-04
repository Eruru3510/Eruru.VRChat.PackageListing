using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

public class AnimationReferenceSearcher : EditorWindow, IEditorOnly {

	const string Name = "Animation Reference Searcher";
	static readonly GUIContent NameGUIContent = new (Name);
	static readonly GUIContent AvatarGUIContent = new ("Avatar");
	static readonly GUIContent PathGUIContent = new ("Path");
	static readonly GUIContent PropertyGUIContent = new ("Property");
	static readonly GUIContent SearchGUIContent = new ("Search");
	static readonly GUIContent CountGUIContent = new ("Count");
	static readonly GUIContent ControllerGUIContent = new ("Controller");
	static readonly GUIContent LayerGUIContent = new ("Layer");
	static readonly GUIContent NodePathGUIContent = new ("Node Path");
	static readonly GUIContent ClipGUIContent = new ("Clip");
	static GUIStyle FoldoutStyle;
	static GUIStyle SelectableLabelStyle;
	static readonly GUILayoutOption SingleLineHeight = GUILayout.Height (EditorGUIUtility.singleLineHeight);
	static readonly RegexOptions RegexOptions = RegexOptions.IgnoreCase | RegexOptions.Compiled;

	VRCAvatarDescriptor VRCAvatarDescriptor;
	string SearchPath = string.Empty;
	string SearchProperty = string.Empty;
	Vector2 Scroll;
	readonly Stack<string> PathNodes = new ();
	readonly Dictionary<string, Result> PathResults = new ();
	readonly Dictionary<string, Vector2> TextSizes = new ();
	AnimatorController CurrentController;
	AnimatorControllerLayer CurrentLayer;
	HashSet<AnimationClip> CurrentClips;

	[MenuItem ("Tools/Eruru/" + Name)]
	static void Open () {
		GetWindow<AnimationReferenceSearcher> (NameGUIContent.text);
	}

	Vector2 GetTextSize (string text, GUIStyle style) {
		if (!TextSizes.TryGetValue (text, out var size)) {
			size = style.CalcSize (EditorGUIUtility.TrTempContent (text));
			TextSizes.Add (text, size);
		}
		return size;
	}

	GUILayoutOption GetTextGUIWidth (string text, GUIStyle style, float margin = 0) {
		return GUILayout.Width (GetTextSize (text, style).x + margin);
	}

	GUILayoutOption GetLabelGUIWidth (string text) {
		return GetTextGUIWidth (text, GUI.skin.label);
	}

	GUILayoutOption GetSelectableTextGUIWidth (string text) {
		return GetTextGUIWidth (text, GUI.skin.label, 5);
	}

	GUILayoutOption GetButtonGUIWidth (string text) {
		return GetTextGUIWidth (text, GUI.skin.button);
	}

	void OnGUI () {
		if (SelectableLabelStyle == null) {
			switch (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToUpperInvariant ()) {
				case "ZH":
					NameGUIContent.text = "动画引用搜索器";
					AvatarGUIContent.text = "模型";
					PathGUIContent.text = "路径";
					PropertyGUIContent.text = "属性";
					SearchGUIContent.text = "搜索";
					CountGUIContent.text = "数量";
					ControllerGUIContent.text = "控制器";
					LayerGUIContent.text = "层";
					NodePathGUIContent.text = "节点路径";
					ClipGUIContent.text = "片段";
					break;
			}
			titleContent = NameGUIContent;
			FoldoutStyle = new (EditorStyles.foldout) { fixedWidth = GetTextSize (string.Empty, EditorStyles.foldout).x };
			SelectableLabelStyle = new (GUI.skin.textField);
			VRCAvatarDescriptor = FindObjectOfType<VRCAvatarDescriptor> ();
		}
		EditorGUILayout.BeginHorizontal ();
		EditorGUILayout.LabelField (AvatarGUIContent, GetLabelGUIWidth (AvatarGUIContent.text));
		VRCAvatarDescriptor = EditorGUILayout.ObjectField (VRCAvatarDescriptor, typeof (VRCAvatarDescriptor), true) as VRCAvatarDescriptor;
		EditorGUILayout.EndHorizontal ();

		EditorGUILayout.BeginHorizontal ();
		EditorGUILayout.LabelField (PathGUIContent, GetLabelGUIWidth (PathGUIContent.text));
		SearchPath = EditorGUILayout.TextField (SearchPath);

		EditorGUILayout.LabelField (PropertyGUIContent, GetLabelGUIWidth (PropertyGUIContent.text));
		SearchProperty = EditorGUILayout.TextField (SearchProperty);

		if (GUILayout.Button (SearchGUIContent, GetButtonGUIWidth (SearchGUIContent.text))) {
			Search ();
		}
		EditorGUILayout.EndHorizontal ();

		Scroll = EditorGUILayout.BeginScrollView (Scroll, GUILayout.ExpandHeight (true));
		foreach (var pathResult in PathResults) {
			EditorGUILayout.BeginHorizontal (EditorStyles.helpBox);
			pathResult.Value.IsExpanded = EditorGUILayout.Foldout (pathResult.Value.IsExpanded, PathGUIContent, true, FoldoutStyle);
			EditorGUILayout.SelectableLabel (
				pathResult.Key,
				SelectableLabelStyle, GetSelectableTextGUIWidth (pathResult.Key), SingleLineHeight
			);

			EditorGUILayout.LabelField (CountGUIContent, GetLabelGUIWidth (CountGUIContent.text), SingleLineHeight);
			EditorGUILayout.SelectableLabel (
				pathResult.Value.CountText,
				SelectableLabelStyle, GetSelectableTextGUIWidth (pathResult.Value.CountText), SingleLineHeight
			);
			EditorGUILayout.EndHorizontal ();
			if (!pathResult.Value.IsExpanded) {
				continue;
			}
			foreach (var item in pathResult.Value.Items) {
				EditorGUILayout.BeginVertical (EditorStyles.helpBox);
				EditorGUILayout.BeginHorizontal ();
				EditorGUILayout.LabelField (ControllerGUIContent, GetLabelGUIWidth (ControllerGUIContent.text), SingleLineHeight);
				EditorGUI.BeginDisabledGroup (true);
				EditorGUILayout.ObjectField (item.Controller, typeof (AnimatorController), true);
				EditorGUI.EndDisabledGroup ();

				EditorGUILayout.LabelField (LayerGUIContent, GetLabelGUIWidth (LayerGUIContent.text), SingleLineHeight);
				EditorGUILayout.SelectableLabel (
					item.Layer.name,
					SelectableLabelStyle, GetSelectableTextGUIWidth (item.Layer.name), SingleLineHeight
				);
				EditorGUILayout.EndHorizontal ();

				EditorGUILayout.BeginHorizontal ();
				EditorGUILayout.LabelField (NodePathGUIContent, GetLabelGUIWidth (NodePathGUIContent.text), SingleLineHeight);
				EditorGUILayout.SelectableLabel (
					item.NodePath,
					SelectableLabelStyle, GetSelectableTextGUIWidth (item.NodePath), SingleLineHeight
				);

				EditorGUILayout.LabelField (ClipGUIContent, GetLabelGUIWidth (ClipGUIContent.text), SingleLineHeight);
				EditorGUI.BeginDisabledGroup (true);
				EditorGUILayout.ObjectField (item.Clip, typeof (AnimationClip), true);
				EditorGUI.EndDisabledGroup ();
				EditorGUILayout.EndHorizontal ();

				EditorGUILayout.BeginHorizontal ();
				EditorGUILayout.LabelField (PropertyGUIContent, GetLabelGUIWidth (PropertyGUIContent.text), SingleLineHeight);
				EditorGUILayout.SelectableLabel (
					item.PropertyName,
					SelectableLabelStyle, GetSelectableTextGUIWidth (item.PropertyName), SingleLineHeight
				);
				EditorGUILayout.EndHorizontal ();
				EditorGUILayout.EndVertical ();
			}
		}
		EditorGUILayout.EndScrollView ();
	}

	void Clear () {
		PathResults.Clear ();
		PathNodes.Clear ();
	}

	void Search () {
		Clear ();
		if (VRCAvatarDescriptor == null) {
			return;
		}
		foreach (var controller in VRCAvatarDescriptor.baseAnimationLayers
			.Concat (VRCAvatarDescriptor.specialAnimationLayers)
			.Select (static x => x.animatorController as AnimatorController)
			.Where (static x => x != null)
		) {
			CurrentController = controller;
			CurrentClips = new (CurrentController.animationClips);
			foreach (var layer in controller.layers) {
				CurrentLayer = layer;
				SearchStateMachine (layer.stateMachine);
			}
			if (CurrentClips.Count > 0) {
				var foundClipCount = CurrentController.animationClips.Length - CurrentClips.Count;
				var path = AssetDatabase.GetAssetPath (CurrentController);
				var count = CurrentController.animationClips.Length;
				EditorUtility.DisplayDialog (
					"Error",
					$"Found Clip Count: {foundClipCount} < {nameof (AnimatorController)}: '{path}' Clip Count: {count}",
					"OK"
				);
			}
		}
		foreach (var pathResult in PathResults) {
			pathResult.Value.CountText = pathResult.Value.Items.Count.ToString ();
			pathResult.Value.Items.Sort (static (a, b) => {
				var result = 0;
				if (result == 0) {
					result = a.Controller.name.CompareTo (b.Controller.name);
				}
				if (result == 0) {
					result = a.Layer.name.CompareTo (b.Layer.name);
				}
				if (result == 0) {
					result = a.Clip.name.CompareTo (b.Clip.name);
				}
				if (result == 0) {
					result = a.NodePath.CompareTo (b.NodePath);
				}
				if (result == 0) {
					result = a.PropertyName.CompareTo (b.PropertyName);
				}
				return result;
			});
		}
	}

	void SearchStateMachine (AnimatorStateMachine stateMachine) {
		foreach (var state in stateMachine.states) {
			SearchState (state.state.motion);
		}
		foreach (var childStateMachine in stateMachine.stateMachines) {
			PathNodes.Push (childStateMachine.stateMachine.name);
			SearchStateMachine (childStateMachine.stateMachine);
			PathNodes.Pop ();
		}
	}

	void SearchState (Motion motion) {
		switch (motion) {
			case AnimationClip clip: {
				CurrentClips.Remove (clip);
				foreach (var binding in AnimationUtility.GetCurveBindings (clip)
					.Concat (AnimationUtility.GetObjectReferenceCurveBindings (clip))
				) {
					var path = binding.path;
					var propertyName = binding.propertyName;
					if ((!string.IsNullOrEmpty (SearchPath) && !Regex.IsMatch (path, SearchPath, RegexOptions))
						|| (!string.IsNullOrEmpty (SearchProperty) && !Regex.IsMatch (propertyName, SearchProperty, RegexOptions))
					) {
						continue;
					}
					if (!PathResults.TryGetValue (path, out var result)) {
						result = new ();
						PathResults.Add (path, result);
					}
					result.Items.Add (new (
						CurrentController, CurrentLayer, string.Join ('/', PathNodes.Reverse ()), clip, path, propertyName
					));
					return;
				}
				break;
			}
			case BlendTree blendTree: {
				PathNodes.Push (blendTree.name);
				foreach (var childMotion in blendTree.children) {
					SearchState (childMotion.motion);
				}
				PathNodes.Pop ();
				break;
			}
		}
	}

	class Result {

		public bool IsExpanded { get; set; }
		public string CountText { get; set; }
		public List<ResultItem> Items { get; } = new ();

	}

	class ResultItem {

		public AnimatorController Controller { get; }
		public AnimatorControllerLayer Layer { get; }
		public string NodePath { get; set; } = string.Empty;
		public AnimationClip Clip { get; }
		public string Path { get; set; } = string.Empty;
		public string PropertyName { get; set; } = string.Empty;
		public bool IsExpanded { get; }

		public ResultItem (
			AnimatorController controller, AnimatorControllerLayer layer, string nodePath, AnimationClip clip,
			string path, string propertyName
		) {
			Controller = controller;
			Layer = layer;
			NodePath = nodePath;
			Clip = clip;
			Path = path;
			PropertyName = propertyName;
		}

	}

}