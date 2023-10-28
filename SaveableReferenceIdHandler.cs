﻿using System;
using System.Collections.Generic;

namespace RasofiaGames.SaveLoadSystem
{
	public class SaveableReferenceIdHandler : IDisposable
	{
		public delegate void StorageLoadHandler(bool wasInStorage, ISaveable instance);
		public delegate void StorageLoadMultipleHandler(ISaveable[] instances);

		public event Action<string, ISaveable> IdForReferenceRequestedEvent;
		public event Action<string> ReferenceRequestedEvent;

		private Dictionary<ISaveable, string> _refToIdMap = new Dictionary<ISaveable, string>();
		private Dictionary<string, ISaveable> _idToRefMap = new Dictionary<string, ISaveable>();
		private Dictionary<string, StorageLoadHandler> _refReadyActions = new Dictionary<string, StorageLoadHandler>();
		private Dictionary<string, MultiRefObject> _multiRefsReadyActions = new Dictionary<string, MultiRefObject>();
		private long _refCounter = 0L;

		public string GetIdForReference(ISaveable reference)
		{
			string refID;
			if(!_refToIdMap.TryGetValue(reference, out refID))
			{
				refID = _refCounter.ToString();
				_refToIdMap.Add(reference, refID);
				_refCounter++;
			}

			if(IdForReferenceRequestedEvent != null)
				IdForReferenceRequestedEvent(refID, reference);

			return refID;
		}

		public void GetReferenceFromID(string refID, StorageLoadHandler callback)
		{
			if(callback == null)
				return;

			ISaveable reference;

			if(_idToRefMap.TryGetValue(refID, out reference))
			{
				callback(true, reference);
			}
			else
			{
				if(!_refReadyActions.ContainsKey(refID))
				{
					_refReadyActions.Add(refID, callback);
				}
				else
				{
					_refReadyActions[refID] += callback;
				}
			}

			if(ReferenceRequestedEvent != null)
			{
				ReferenceRequestedEvent(refID);
			}
		}

		public void GetReferencesFromID(string refHolder, string[] refIDs, StorageLoadMultipleHandler callback)
		{
			if(callback == null)
				return;

			if(refIDs == null || refIDs.Length == 0)
			{
				callback(null);
				return;
			}

			_multiRefsReadyActions.Add(refHolder, new MultiRefObject(refIDs, callback));

			string refHolderIdReference = refHolder;

			for(int i = 0; i < refIDs.Length; i++)
			{
				string referenceID = refIDs[i];
				GetReferenceFromID(referenceID, new StorageLoadHandler((wasInStorage, instance) => 
				{
					MultiRefObject refsObject;
					if(_multiRefsReadyActions.TryGetValue(refHolderIdReference, out refsObject))
					{
						if(refsObject.CrossRefAway(referenceID, instance))
						{
							_multiRefsReadyActions.Remove(refHolderIdReference);
							refsObject.Clean();
						}
					}
				}));
			}
		}

		public void SetReferenceReady(ISaveable refToSetReady, string refID = null)
		{
			if(string.IsNullOrEmpty(refID))
				refID = GetIdForReference(refToSetReady);

			if(!_idToRefMap.ContainsKey(refID))
				_idToRefMap.Add(refID, refToSetReady);

			if(_refReadyActions.ContainsKey(refID))
			{
				_refReadyActions[refID](true, _idToRefMap[refID]);
				_refReadyActions.Remove(refID);
			}
		}

		public void LoadRemainingAsNull()
		{
			foreach(var pair in _refReadyActions)
			{
				pair.Value(false, null);
			}
		}

		public void Dispose()
		{
			foreach(var pair in _multiRefsReadyActions)
			{
				pair.Value.Clean();
			}

			_multiRefsReadyActions.Clear();
			_multiRefsReadyActions = null;

			_refToIdMap.Clear();
			_idToRefMap.Clear();
			_refReadyActions.Clear();

			_refToIdMap = null;
			_idToRefMap = null;
			_refReadyActions = null;

			IdForReferenceRequestedEvent = null;
			ReferenceRequestedEvent = null;

			_refCounter = 0L;
		}

		private class MultiRefObject
		{
			private StorageLoadMultipleHandler _callback;
			private List<string> _refsToGo;
			private List<ISaveable> _references = new List<ISaveable>();

			public MultiRefObject(string[] refsToGo, StorageLoadMultipleHandler callback)
			{
				_refsToGo = new List<string>(refsToGo);
				_callback = callback;
			}

			public bool CrossRefAway(string referenceId, ISaveable referenceInstance)
			{
				_refsToGo.Remove(referenceId);
				_references.Add(referenceInstance);
				if(_refsToGo.Count == 0)
				{
					_callback(_references.ToArray());
					return true;
				}

				return false;
			}

			public void Clean()
			{
				_refsToGo.Clear();
				_refsToGo = null;
				_callback = null;
				_references.Clear();
				_references = null;
			}
		}
	}
}