﻿using System;
using UnityEditor;
using UnityEngine;

namespace RasofiaGames.SaveLoadSystem.Internal.Utils
{
	public static class EditorMenu
	{
		public const string BASE_ROUTE = "Storage/";

		public const string AUTO_VALIDATE_STORAGE_PREF = "EditorMenu_AutoValidateStorage";
		public const string VALIDATE_STORAGE_LOCATION_PREF = "EditorMenu_Location_ValidateStorage";

		[InitializeOnLoadMethod]
		public static void AutoValidateStorage()
		{
			if (!EditorPrefs.HasKey(VALIDATE_STORAGE_LOCATION_PREF))
			{
				SetValidateStorageLocation();
			}
			else if (ShouldRunAutoStorageValidation())
			{
				ValidateStorage(StorageInspectorEditor.CorruptionState.Warning | StorageInspectorEditor.CorruptionState.Error);
			}
		}

		[MenuItem(BASE_ROUTE + "/", priority = 201)]
		public static void TurnXAutoValidationSeparator() { } // Empty method for the separator

		[MenuItem(BASE_ROUTE + "Toggle Auto Storage Validation/ON", priority = 200)]
		public static void TurnOnAutoValidateStorage()
		{
			SetRunAutoStorageValidation(true);
		}

		[MenuItem(BASE_ROUTE + "Toggle Auto Storage Validation/OFF", priority = 200)]
		public static void TurnOffAutoValidateStorage()
		{
			SetRunAutoStorageValidation(false);
		}

		[MenuItem(BASE_ROUTE + "Set Validate Storage Location", priority = 200)]
		public static void SetValidateStorageLocation()
		{
			InputWindow.OpenWindow(GetStorageLocation(), (newLocation) =>
			{
				SetStorageLocation(newLocation);
			}, titleLabel: "Validate Storage Location", buttonLabel: null);
		}

		[MenuItem(BASE_ROUTE + "Toggle Auto Storage Validation/ON", validate = true)]
		public static bool ValidateTurnOnAutoValidateStorage()
		{
			return !ShouldRunAutoStorageValidation();
		}

		[MenuItem(BASE_ROUTE + "Toggle Auto Storage Validation/OFF", validate = true)]
		public static bool ValidateTurnOffAutoValidateStorage()
		{
			return ShouldRunAutoStorageValidation();
		}

		[MenuItem(BASE_ROUTE + "Validate Storage", priority = 200)]
		public static void ValidateStorage()
		{
			ValidateStorage(StorageInspectorEditor.CorruptionState.None | StorageInspectorEditor.CorruptionState.Warning | StorageInspectorEditor.CorruptionState.Error);
		}

		private static bool ShouldRunAutoStorageValidation()
		{
			return EditorPrefs.GetBool(AUTO_VALIDATE_STORAGE_PREF, true);
		}

		private static void SetRunAutoStorageValidation(bool state)
		{
			EditorPrefs.SetBool(AUTO_VALIDATE_STORAGE_PREF, state);
		}

		private static string GetStorageLocation()
		{
			return EditorPrefs.GetString(VALIDATE_STORAGE_LOCATION_PREF, string.Empty);
		}

		private static void SetStorageLocation(string location)
		{
			EditorPrefs.SetString(VALIDATE_STORAGE_LOCATION_PREF, location);
		}

		private static void ValidateStorage(StorageInspectorEditor.CorruptionState corruptionStateOpenTriggers)
		{
			switch (StorageInspectorEditor.ValidateStorage(GetStorageLocation(), Storage.EncodingType.Base64, corruptionStateOpenTriggers))
			{
				case StorageInspectorEditor.CorruptionState.Error:
					Debug.LogError("Storage Contains an Error!");
					break;
				case StorageInspectorEditor.CorruptionState.Warning:
					Debug.LogWarning("Storage Contains a Warning!");
					break;
				case StorageInspectorEditor.CorruptionState.None:
					Debug.Log("<color=green>Storage is valid!</color>");
					break;
			}
		}

		private class InputWindow : EditorWindow
		{
			private string _currentValue;
			private Action<string> _closeCallback;
			private string _buttonLabel = null;

			public static InputWindow OpenWindow(string currentValue, Action<string> closeCallback, string titleLabel = null, string buttonLabel = null)
			{
				InputWindow window = GetWindow<InputWindow>();
				window.titleContent = new GUIContent(string.IsNullOrEmpty(titleLabel) ? "Input Field" : titleLabel);
				window.Show();
				window.Focus();
				window._buttonLabel = buttonLabel;
				window._currentValue = currentValue;
				window._closeCallback = closeCallback;
				return window;
			}

			protected void OnGUI()
			{
				_currentValue = EditorGUILayout.TextField(_currentValue);
				if (GUILayout.Button(string.IsNullOrEmpty(_buttonLabel) ? "Submit" : _buttonLabel))
				{
					Close();
				}
			}

			protected void OnDestroy()
			{
				_closeCallback?.Invoke(_currentValue);
			}
		}
	}
}