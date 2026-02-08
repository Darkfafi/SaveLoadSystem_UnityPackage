using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using static RasofiaGames.SaveLoadSystem.Internal.StorageKeySearcher;

namespace RasofiaGames.SaveLoadSystem.Internal.Utils
{
	public class StorageInspectorEditor : EditorWindow
	{
		[Flags]
		public enum CorruptionState
		{
			None = 1 << 0,
			Warning = 1 << 1,
			Error = 1 << 2,
		}
		
		private const string TYPE_NOT_FOUND_INFO_MESSAGE = "Type not found in project";
		private const string EXPECTED_TYPE_INFO_MESSAGE_F = "Expected type {0} but found type {1}";
		private const string KEY_VALIDATION_CORRUPT_INFO_MESSAGE = "Key Validation is corrupt (Check the `StorageKeyAttribute` usage)";
		private const string KEY_HAS_DUPLICATE_F = "Key {0} has a duplicate!";

		private static class Styles
		{
			public static GUIStyle RowButton;
			public static GUIStyle Breadcrumb;
			public static GUIStyle IconLabel;

			static Styles()
			{
				RowButton = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true };
				Breadcrumb = new GUIStyle(EditorStyles.toolbarButton);
				IconLabel = new GUIStyle(EditorStyles.label) { padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(4, 0, 2, 0) };
			}
		}

		// --- State ---
		private RootItem _rootItem;
		private BaseItem _currentDir;
		private List<BaseItem> _searchResults = new List<BaseItem>();
		private bool _isSearching = false;

		private string _pathInputValue = string.Empty;
		private Storage.EncodingType _encodingTypeInputValue = Storage.EncodingType.Base64;
		private SearchField _searchField;
		private string _searchString = "";
		private string _lastExecutedSearch = "";
		private Vector2 _scroll = Vector2.zero;

		private static StorageInspectorEditor _currentlyOpenStorageInspector = null;

		[MenuItem(EditorMenu.BASE_ROUTE + "Open Storage Inspector")]
		private static void Init() => OpenWindow();

		private static StorageInspectorEditor OpenWindow()
		{
			if (_currentlyOpenStorageInspector != null) { _currentlyOpenStorageInspector.Focus(); return _currentlyOpenStorageInspector; }
			StorageInspectorEditor window = GetWindow<StorageInspectorEditor>();
			window.titleContent = new GUIContent("Storage Inspector");
			window.Show();
			_currentlyOpenStorageInspector = window;
			return window;
		}

		public static CorruptionState ValidateStorage(string storagePath, Storage.EncodingType encodingType, CorruptionState openOnState = CorruptionState.Warning | CorruptionState.Error)
		{
			CorruptionState worstState = CorruptionState.None;
			CapsuleItem[] items = LoadCapsuleItemsInternal(storagePath, encodingType, GetStorageCapsuleInstances());
			foreach (var item in items) if (item.State > worstState) worstState = item.State;

			if ((worstState & openOnState) != 0)
			{
				var window = OpenWindow();
				window._pathInputValue = storagePath;
				window._encodingTypeInputValue = encodingType;
				window.LoadStorage(storagePath, encodingType);
			}
			return worstState;
		}

		protected void OnEnable()
		{
			if (_searchField == null) _searchField = new SearchField();
			if (_rootItem == null) _rootItem = new RootItem();
			if (_currentDir == null) _currentDir = _rootItem;
			_pathInputValue = EditorPrefs.GetString(EditorMenu.VALIDATE_STORAGE_LOCATION_PREF, string.Empty);
		}

		protected void OnGUI()
		{
			DrawControls();
			DrawHeader();

			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			if (_isSearching) DrawSearchResults();
			else DrawCurrentDirectory();
			EditorGUILayout.EndScrollView();
		}

		private void DrawControls()
		{
			EditorGUILayout.BeginVertical(GUI.skin.box);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Path:", GUILayout.Width(50));
			_pathInputValue = EditorGUILayout.TextField(_pathInputValue);
			if (GUILayout.Button("Open", GUILayout.Width(60)))
			{
				string path = Storage.GetPathToStorage(_pathInputValue);
				if (System.IO.Directory.Exists(path)) EditorUtility.RevealInFinder(path);
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Enc:", GUILayout.Width(50));
			_encodingTypeInputValue = (Storage.EncodingType)EditorGUILayout.EnumPopup(_encodingTypeInputValue, GUILayout.Width(100));
			if (GUILayout.Button("Load")) LoadStorage(_pathInputValue, _encodingTypeInputValue);
			if (GUILayout.Button("Clear"))
			{
				if (EditorUtility.DisplayDialog("Clear Storage", "Delete all save files?", "Yes", "No"))
					ClearCapsuleItems(_pathInputValue, _encodingTypeInputValue, GetStorageCapsuleInstances());
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
		}

		private void DrawHeader()
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			bool enterPressed = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
			string newSearch = _searchField.OnToolbarGUI(_searchString);
			if (newSearch != _searchString || enterPressed)
			{
				_searchString = newSearch;
				if (enterPressed || string.IsNullOrEmpty(_searchString)) { UpdateSearch(); GUI.FocusControl(null); }
			}
			if (GUILayout.Button("Search", EditorStyles.toolbarButton, GUILayout.Width(50))) UpdateSearch();
			GUILayout.EndHorizontal();

			if (!_isSearching)
			{
				GUILayout.BeginHorizontal(EditorStyles.toolbar);
				List<BaseItem> path = new List<BaseItem>();
				BaseItem walker = _currentDir;
				while (walker != null) { path.Add(walker); walker = walker.Parent; }
				path.Reverse();
				for (int i = 0; i < path.Count; i++)
				{
					if (i > 0) GUILayout.Label(">", EditorStyles.miniLabel, GUILayout.Width(10));
					if (GUILayout.Button(path[i].Key, Styles.Breadcrumb, GUILayout.ExpandWidth(false))) NavigateTo(path[i]);
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}

		private void DrawCurrentDirectory()
		{
			if (_currentDir.Children.Count == 0) GUILayout.Label("No Items", EditorStyles.centeredGreyMiniLabel);
			foreach (var child in _currentDir.Children) child.DrawRow(NavigateTo);
		}

		private void DrawSearchResults()
		{
			GUILayout.Label($"Results for '{_lastExecutedSearch}': {_searchResults.Count}", EditorStyles.boldLabel);
			foreach (var item in _searchResults)
			{
				EditorGUILayout.BeginHorizontal();
				GUILayout.Label(item.GetFullPath(), EditorStyles.miniLabel, GUILayout.Width(200));
				if (GUILayout.Button(item.Key, Styles.RowButton)) { NavigateTo(item.IsContainer ? item : item.Parent); _isSearching = false; }
				EditorGUILayout.EndHorizontal();
			}
		}

		private void NavigateTo(BaseItem item) { _currentDir = item; _scroll = Vector2.zero; }

		private void UpdateSearch()
		{
			_lastExecutedSearch = _searchString;
			if (string.IsNullOrEmpty(_searchString)) { _isSearching = false; _searchResults.Clear(); return; }
			_isSearching = true;
			_searchResults.Clear();
			SearchRecursive(_rootItem);
		}

		private void SearchRecursive(BaseItem current)
		{
			foreach (var child in current.Children)
			{
				if (child.Key.IndexOf(_searchString, StringComparison.OrdinalIgnoreCase) >= 0 ||
					child.GetSearchableContent().IndexOf(_searchString, StringComparison.OrdinalIgnoreCase) >= 0) _searchResults.Add(child);
				if (child.IsContainer) SearchRecursive(child);
			}
		}

		public void LoadStorage(string path, Storage.EncodingType encodingType)
		{
			_rootItem = new RootItem();
			CapsuleItem[] items = LoadCapsuleItemsInternal(path, encodingType, GetStorageCapsuleInstances());
			_rootItem.Children.AddRange(items);
			foreach (var item in items) item.SetParent(_rootItem);
			_currentDir = _rootItem;
			_isSearching = false;
		}

		private static CapsuleItem[] LoadCapsuleItemsInternal(string storagePath, Storage.EncodingType encodingType, IStorageCapsule[] storageCapsules)
		{
			List<CapsuleItem> loadedItems = new List<CapsuleItem>();
			Storage storage = new Storage(storagePath, encodingType, storageCapsules);
			List<ReadStorageResult> results = storage.Read(storageCapsules.Select(x => x.ID).ToArray());
			foreach (var result in results)
			{
				var capsuleType = storageCapsules.FirstOrDefault(x => x.ID == result.CapsuleID)?.GetType();
				if (capsuleType != null) loadedItems.Add(new CapsuleItem(result.CapsuleID, result.CapsuleStorage, GetKeyEntries(capsuleType)));
			}
			return loadedItems.ToArray();
		}

		private static IStorageCapsule[] GetStorageCapsuleInstances() =>
			AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
				.Where(x => typeof(IStorageCapsule).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
				.Select(t => Activator.CreateInstance(t) as IStorageCapsule).ToArray();

		private static void ClearCapsuleItems(string storagePath, Storage.EncodingType encodingType, IStorageCapsule[] storageCapsules) =>
			new Storage(storagePath, encodingType, storageCapsules).Clear(true, storageCapsules.Select(x => x.ID).ToArray());

		#region Classes

		private abstract class BaseItem
		{
			public string Key { get; protected set; }
			public BaseItem Parent { get; protected set; }
			public List<BaseItem> Children { get; } = new List<BaseItem>();
			public virtual bool IsContainer => Children.Count > 0;
			public CorruptionState State { get; protected set; } = CorruptionState.None;
			public string CorruptionReason { get; protected set; } = "";

			public BaseItem(string key, BaseItem parent) { Key = key; Parent = parent; }
			public void SetParent(BaseItem parent) => Parent = parent;
			public string GetFullPath() => Parent == null || Parent is RootItem ? Key : $"{Parent.GetFullPath()}/{Key}";
			public virtual string GetSearchableContent() => "";

			public virtual void DrawRow(Action<BaseItem> onNavigate)
			{
				EditorGUILayout.BeginHorizontal();

				// Draw Status Icon with Tooltip
				if (State != CorruptionState.None)
				{
					string iconStr = State == CorruptionState.Error ? "[!]" : "[?]";
					Color color = State == CorruptionState.Error ? Color.red : new Color(1f, 0.65f, 0f);
					var content = new GUIContent(iconStr, CorruptionReason);

					var oldColor = GUI.color;
					GUI.color = color;
					GUILayout.Label(content, Styles.IconLabel, GUILayout.Width(20));
					GUI.color = oldColor;
				}
				else
				{
					GUILayout.Space(24);
				}

				string label = IsContainer ? $"<b>{Key}</b>/" : Key;
				string extra = GetExtraInfo();
				if (!string.IsNullOrEmpty(extra)) label += $" <color=#888888>{extra}</color>";

				if (GUILayout.Button(label, Styles.RowButton)) if (IsContainer) onNavigate(this);

				EditorGUILayout.EndHorizontal();
			}
			protected virtual string GetExtraInfo() => "";
		}

		private class RootItem : BaseItem { public RootItem() : base("Root", null) { } }

		private class CapsuleItem : BaseItem
		{
			public CapsuleItem(string id, IStorageDictionaryEditor storage, Dictionary<string, StorageKeyEntry> keyEntries) : base(id, null)
			{
				var storageItem = new StorageItem(id, this, storage, keyEntries);
				Children.AddRange(storageItem.Children);
				foreach (var c in Children) c.SetParent(this);
				State = storageItem.State;
				CorruptionReason = storageItem.CorruptionReason;
			}
		}

		private class StorageItem : BaseItem
		{
			public StorageItem(string key, BaseItem parent, IStorageDictionaryEditor storage, Dictionary<string, StorageKeyEntry> keys) : base(key, parent)
			{
				// Track which keys we actually find to detect missing ones
				HashSet<string> foundKeys = new HashSet<string>();

				foreach (var k in storage.GetValueStorageKeys())
				{
					foundKeys.Add(k);
					keys.TryGetValue(k, out var entry);
					var item = new ValKeyItem(entry.IsValid ? entry : new StorageKeyEntry { StorageKey = k }, this, storage.GetValueSection(k));
					Children.Add(item);
					if (item.State > State) { State = item.State; CorruptionReason = item.CorruptionReason; }
				}

				foreach (var k in storage.GetRefStorageKeys())
				{
					foundKeys.Add(k);
					keys.TryGetValue(k, out var entry);
					var item = new RefsKeyItem(entry.IsValid ? entry : new StorageKeyEntry { StorageKey = k }, this, storage.GetValueRefs(k));
					Children.Add(item);
					if (item.State > State) { State = item.State; CorruptionReason = item.CorruptionReason; }
				}

				// Check for Missing Keys (Restored Logic)
				var missing = keys.Values.Where(x => !x.IsOptional && !foundKeys.Contains(x.StorageKey)).ToList();
				if (missing.Count > 0 && State != CorruptionState.Error)
				{
					State = CorruptionState.Warning;
					StringBuilder sb = new StringBuilder("Missing keys:\n");
					foreach (var m in missing) sb.AppendLine($"- {m.StorageKey}");
					CorruptionReason = sb.ToString();
				}
			}
		}

		private class ValKeyItem : BaseItem
		{
			private string _valStr;
			public ValKeyItem(StorageKeyEntry e, BaseItem p, SaveableValueSection s) : base(e.StorageKey, p)
			{
				_valStr = s.ValueString;

				// Apply Exact Validation Logic
				GetCorruptStateWithInfo(e, s, out CorruptionState state, out string info);
				State = state;
				CorruptionReason = info;

				// Child Structure for Navigation
				Type t = s.GetSafeValueType();
				if (t == typeof(SaveableDict))
					foreach (var item in ((SaveableDict)s.GetValue()).Items) Children.Add(new DictEntryItem(item, this));
				else if (t == typeof(SaveableArray))
				{
					var arr = ((SaveableArray)s.GetValue()).Items;
					for (int i = 0; i < arr.Length; i++) Children.Add(new ArrayEntryItem(i, arr[i], this));
				}
			}

			private void GetCorruptStateWithInfo(StorageKeyEntry keyEntry, SaveableValueSection valueSection, out CorruptionState state, out string info)
			{
				if (!keyEntry.IsValid) { state = CorruptionState.Warning; info = KEY_VALIDATION_CORRUPT_INFO_MESSAGE; return; }
				if (keyEntry.HasDuplicate) { state = CorruptionState.Error; info = string.Format(KEY_HAS_DUPLICATE_F, keyEntry.StorageKey); return; }

				Type safeType = valueSection.GetSafeValueType();
				if (safeType == null || keyEntry.GetExpectedType() == null) { state = CorruptionState.Error; info = TYPE_NOT_FOUND_INFO_MESSAGE; return; }

				// Dict Logic
				if (safeType == typeof(SaveableDict))
				{
					var dictVal = (SaveableDict)valueSection.GetValue();
					if (dictVal.Items.Length > 0 && keyEntry.TryGetExpectedDictTypes(out Type exKeyType, out Type exValType))
					{
						var item = dictVal.Items[0];
						Type tKey = item.KeySection.GetSafeValueType();
						Type tValue = item.ValueSection.GetSafeValueType();

						bool keyOk = tKey != null && exKeyType.IsAssignableFrom(tKey);
						bool valOk = tValue != null && exValType.IsAssignableFrom(tValue);

						if (!keyOk) { state = CorruptionState.Error; info = tKey == null ? TYPE_NOT_FOUND_INFO_MESSAGE : string.Format(EXPECTED_TYPE_INFO_MESSAGE_F, exKeyType.Name, tKey.Name); return; }
						if (!valOk) { state = CorruptionState.Error; info = tValue == null ? TYPE_NOT_FOUND_INFO_MESSAGE : string.Format(EXPECTED_TYPE_INFO_MESSAGE_F, exValType.Name, tValue.Name); return; }
					}
					state = CorruptionState.None; info = ""; return;
				}

				// Array Logic
				if (safeType == typeof(SaveableArray))
				{
					var arrVal = (SaveableArray)valueSection.GetValue();
					if (arrVal.Items.Length > 0 && keyEntry.TryGetExpectedArrayType(out Type exArrType))
					{
						var item = arrVal.Items[0];
						Type tItem = item.GetSafeValueType();
						if (tItem == null || !exArrType.IsAssignableFrom(tItem))
						{
							state = CorruptionState.Error;
							info = tItem == null ? TYPE_NOT_FOUND_INFO_MESSAGE : string.Format(EXPECTED_TYPE_INFO_MESSAGE_F, exArrType.Name, tItem.Name);
							return;
						}
					}
					state = CorruptionState.None; info = ""; return;
				}

				// Standard Value Logic
				state = keyEntry.IsOfExpectedType(safeType) ? CorruptionState.None : CorruptionState.Error;
				info = state == CorruptionState.None ? "" : string.Format(EXPECTED_TYPE_INFO_MESSAGE_F, keyEntry.GetExpectedType().Name, safeType.Name);
			}

			public override string GetSearchableContent() => _valStr;
			protected override string GetExtraInfo() => Children.Count > 0 ? $"[{Children.Count}]" : $"= {_valStr}";
		}

		private class RefsKeyItem : BaseItem
		{
			public RefsKeyItem(StorageKeyEntry e, BaseItem p, EditableRefValue[] r) : base(e.StorageKey, p)
			{
				foreach (var rv in r) Children.Add(new RefItem(e, this, rv));

				// Fix from User
				if (Children.Count > 0)
				{
					State = Children.Max(x => x.State);
					if (State != CorruptionState.None) CorruptionReason = Children.First(x => x.State == State).CorruptionReason;
				}
				else
				{
					State = CorruptionState.None;
				}
			}
		}

		private class RefItem : BaseItem
		{
			public RefItem(StorageKeyEntry e, BaseItem p, EditableRefValue rv) : base(rv.ReferenceID, p)
			{
				if (rv.ReferenceType == null) { State = CorruptionState.Error; CorruptionReason = TYPE_NOT_FOUND_INFO_MESSAGE; }
				else if (!e.IsValid) { State = CorruptionState.Warning; CorruptionReason = KEY_VALIDATION_CORRUPT_INFO_MESSAGE; }
				else if (!e.IsOfExpectedType(rv.ReferenceType)) { State = CorruptionState.Error; CorruptionReason = string.Format(EXPECTED_TYPE_INFO_MESSAGE_F, e.GetExpectedType().Name, rv.ReferenceType.Name); }

				var inner = new StorageItem("Inner", this, rv.Storage, GetKeyEntries(rv.ReferenceType));
				Children.AddRange(inner.Children);
				foreach (var c in Children) c.SetParent(this);

				if (inner.State > State) { State = inner.State; CorruptionReason = inner.CorruptionReason; }
				if (e.HasDuplicate) { State = CorruptionState.Error; CorruptionReason = string.Format(KEY_HAS_DUPLICATE_F, e.StorageKey); }
			}
			protected override string GetExtraInfo() => "(Ref)";
		}

		private class DictEntryItem : BaseItem
		{
			private string _v;
			public DictEntryItem(DictItem i, BaseItem p) : base(i.KeySection.ValueString, p) => _v = i.ValueSection.ValueString;
			public override string GetSearchableContent() => _v;
			protected override string GetExtraInfo() => $"= {_v}";
		}

		private class ArrayEntryItem : BaseItem
		{
			private string _v;
			public ArrayEntryItem(int i, SaveableValueSection s, BaseItem p) : base($"[{i}]", p) => _v = s.ValueString;
			public override string GetSearchableContent() => _v;
			protected override string GetExtraInfo() => $"= {_v}";
		}

		#endregion
	}
}