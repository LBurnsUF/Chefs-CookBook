// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.NetworkUIPromptController
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HG;
using JetBrains.Annotations;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

public class NetworkUIPromptController : NetworkBehaviour
{
	private struct LocalUserInfo
	{
		public LocalUser localUser;

		public NetworkUIPromptController currentController;
	}

	private float lastCurrentLocalParticipantUpdateTime = float.NegativeInfinity;

	private LocalUser _currentLocalParticipant;

	private CharacterMaster _currentParticipantMaster;

	private bool _inControl;

	[SyncVar(hook = "SetParticipantMasterId")]
	private NetworkInstanceId masterObjectInstanceId;

	private CameraRigController _currentCamera;

	private static LocalUserInfo[] allLocalUserInfo = Array.Empty<LocalUserInfo>();

	private static int allLocalUserInfoCount = 0;

	public Action<NetworkReader> messageFromClientHandler;

	private LocalUser currentLocalParticipant
	{
		get
		{
			return _currentLocalParticipant;
		}
		set
		{
			if (_currentLocalParticipant != value)
			{
				if (_currentLocalParticipant != null)
				{
					OnLocalParticipantLost(_currentLocalParticipant);
				}
				_currentLocalParticipant = value;
				if (_currentLocalParticipant != null)
				{
					OnLocalParticipantDiscovered(_currentLocalParticipant);
				}
			}
		}
	}

	public CharacterMaster currentParticipantMaster
	{
		get
		{
			return _currentParticipantMaster;
		}
		private set
		{
			if ((object)_currentParticipantMaster != value)
			{
				if ((object)_currentParticipantMaster != null)
				{
					OnParticipantLost(_currentParticipantMaster);
				}
				_currentParticipantMaster = value;
				if ((object)_currentParticipantMaster != null)
				{
					OnParticipantDiscovered(_currentParticipantMaster);
				}
			}
		}
	}

	private bool inControl
	{
		get
		{
			return _inControl;
		}
		set
		{
			if (_inControl != value)
			{
				_inControl = value;
				if (_inControl)
				{
					OnControlBegin();
				}
				else
				{
					OnControlEnd();
				}
			}
		}
	}

	private CameraRigController currentCamera
	{
		get
		{
			return _currentCamera;
		}
		set
		{
			if ((object)_currentCamera != value)
			{
				if ((object)_currentCamera != null)
				{
					this.onDisplayEnd?.Invoke(this, currentLocalParticipant, _currentCamera);
				}
				_currentCamera = value;
				if ((object)_currentCamera != null)
				{
					this.onDisplayBegin?.Invoke(this, currentLocalParticipant, _currentCamera);
				}
			}
		}
	}

	public bool inUse => currentParticipantMaster;

	public bool isDisplaying => currentCamera;

	public NetworkInstanceId NetworkmasterObjectInstanceId
	{
		get
		{
			return masterObjectInstanceId;
		}
		[param: In]
		set
		{
			if (NetworkServer.localClientActive && !base.syncVarHookGuard)
			{
				base.syncVarHookGuard = true;
				SetParticipantMasterId(value);
				base.syncVarHookGuard = false;
			}
			SetSyncVar(value, ref masterObjectInstanceId, 1u);
		}
	}

	public event Action<NetworkUIPromptController, LocalUser, CameraRigController> onDisplayBegin;

	public event Action<NetworkUIPromptController, LocalUser, CameraRigController> onDisplayEnd;

	private void OnParticipantDiscovered([NotNull] CharacterMaster master)
	{
		LocalUser localUser = null;
		if ((bool)master.playerCharacterMasterController && (bool)master.playerCharacterMasterController.networkUser)
		{
			localUser = master.playerCharacterMasterController.networkUser.localUser;
		}
		currentLocalParticipant = localUser;
	}

	private void OnParticipantLost([NotNull] CharacterMaster master)
	{
		currentLocalParticipant = null;
	}

	private void OnLocalParticipantDiscovered([NotNull] LocalUser localUser)
	{
		lastCurrentLocalParticipantUpdateTime = Time.unscaledTime;
		UpdateBestControllerForLocalUser(localUser);
	}

	private void OnLocalParticipantLost([NotNull] LocalUser localUser)
	{
		ref LocalUserInfo localUserInfo = ref GetLocalUserInfo(localUser);
		if (localUserInfo.currentController == this)
		{
			localUserInfo.currentController.inControl = false;
			localUserInfo.currentController = null;
		}
	}

	private void HandleCameraDiscovered(CameraRigController cameraRigController)
	{
		currentCamera = cameraRigController;
	}

	private void HandleCameraLost(CameraRigController cameraRigController)
	{
		currentCamera = null;
	}

	private void OnControlBegin()
	{
		currentCamera = currentLocalParticipant.cameraRigController;
		currentLocalParticipant.onCameraDiscovered += HandleCameraDiscovered;
		currentLocalParticipant.onCameraLost += HandleCameraLost;
	}

	private void OnControlEnd()
	{
		currentLocalParticipant.onCameraLost -= HandleCameraLost;
		currentLocalParticipant.onCameraDiscovered -= HandleCameraDiscovered;
		currentCamera = null;
	}

	[CanBeNull]
	private static NetworkUIPromptController FindBestControllerForLocalUser([NotNull] LocalUser localUser)
	{
		NetworkUIPromptController result = null;
		float num = float.PositiveInfinity;
		List<NetworkUIPromptController> instancesList = InstanceTracker.GetInstancesList<NetworkUIPromptController>();
		for (int i = 0; i < instancesList.Count; i++)
		{
			NetworkUIPromptController networkUIPromptController = instancesList[i];
			if (networkUIPromptController.currentLocalParticipant == localUser && networkUIPromptController.lastCurrentLocalParticipantUpdateTime < num)
			{
				num = networkUIPromptController.lastCurrentLocalParticipantUpdateTime;
				result = networkUIPromptController;
			}
		}
		return result;
	}

	private static void UpdateBestControllerForLocalUser([NotNull] LocalUser localUser)
	{
		ref LocalUserInfo localUserInfo = ref GetLocalUserInfo(localUser);
		NetworkUIPromptController currentController = localUserInfo.currentController;
		NetworkUIPromptController networkUIPromptController = FindBestControllerForLocalUser(localUser);
		if ((object)currentController != networkUIPromptController)
		{
			if ((object)currentController != null)
			{
				currentController.inControl = false;
			}
			if ((object)networkUIPromptController != null)
			{
				networkUIPromptController.inControl = true;
			}
			localUserInfo.currentController = networkUIPromptController;
		}
	}

	private void OnEnable()
	{
		InstanceTracker.Add(this);
	}

	private void OnDisable()
	{
		SetParticipantMasterId(NetworkInstanceId.Invalid);
		InstanceTracker.Remove(this);
	}

	public override void OnStartClient()
	{
		base.OnStartClient();
		if (!NetworkServer.active)
		{
			SetParticipantMasterId(masterObjectInstanceId);
		}
	}

	private void SetParticipantMasterId(NetworkInstanceId newMasterObjectInstanceId)
	{
		NetworkmasterObjectInstanceId = newMasterObjectInstanceId;
		GameObject gameObject = Util.FindNetworkObject(masterObjectInstanceId);
		CharacterMaster characterMaster = null;
		if ((bool)gameObject)
		{
			characterMaster = gameObject.GetComponent<CharacterMaster>();
		}
		currentParticipantMaster = characterMaster;
	}

	[Server]
	public void ClearParticipant()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.NetworkUIPromptController::ClearParticipant()' called on client");
		}
		else
		{
			SetParticipantMaster(null);
		}
	}

	[Server]
	public void SetParticipantMaster([CanBeNull] CharacterMaster newParticipantMaster)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.NetworkUIPromptController::SetParticipantMaster(RoR2.CharacterMaster)' called on client");
			return;
		}
		NetworkIdentity networkIdentity = (newParticipantMaster ? newParticipantMaster.networkIdentity : null);
		SetParticipantMasterId(networkIdentity ? networkIdentity.netId : NetworkInstanceId.Invalid);
	}

	[Server]
	public void SetParticipantMasterFromInteractor([CanBeNull] Interactor newParticipantInteractor)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.NetworkUIPromptController::SetParticipantMasterFromInteractor(RoR2.Interactor)' called on client");
			return;
		}
		CharacterMaster participantMaster = ((!newParticipantInteractor) ? null : newParticipantInteractor.GetComponent<CharacterBody>()?.master);
		SetParticipantMaster(participantMaster);
	}

	[Server]
	public void SetParticipantMasterFromInteractorObject([CanBeNull] UnityEngine.Object newParticipantInteractor)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.NetworkUIPromptController::SetParticipantMasterFromInteractorObject(UnityEngine.Object)' called on client");
		}
		else
		{
			SetParticipantMasterFromInteractor(newParticipantInteractor as Interactor);
		}
	}

	[SystemInitializer(new Type[] { })]
	private static void Init()
	{
		LocalUserManager.onUserSignIn += OnUserSignIn;
		LocalUserManager.onUserSignOut += OnUserSignOut;
	}

	private static void OnUserSignIn(LocalUser localUser)
	{
		LocalUserInfo value = new LocalUserInfo
		{
			localUser = localUser,
			currentController = null
		};
		ArrayUtils.ArrayAppend(ref allLocalUserInfo, ref allLocalUserInfoCount, in value);
	}

	private static void OnUserSignOut(LocalUser localUser)
	{
		for (int i = 0; i < allLocalUserInfoCount; i++)
		{
			if (allLocalUserInfo[i].localUser == localUser)
			{
				ArrayUtils.ArrayRemoveAt(allLocalUserInfo, ref allLocalUserInfoCount, i);
				break;
			}
		}
	}

	private static ref LocalUserInfo GetLocalUserInfo(LocalUser localUser)
	{
		for (int i = 0; i < allLocalUserInfoCount; i++)
		{
			if (allLocalUserInfo[i].localUser == localUser)
			{
				return ref allLocalUserInfo[i];
			}
		}
		throw new ArgumentException("localUser must be signed in");
	}

	[Client]
	public NetworkWriter BeginMessageToServer()
	{
		if (!NetworkClient.active)
		{
			Debug.LogWarning("[Client] function 'UnityEngine.Networking.NetworkWriter RoR2.NetworkUIPromptController::BeginMessageToServer()' called on server");
			return null;
		}
		NetworkWriter networkWriter = new NetworkWriter();
		networkWriter.StartMessage(76);
		networkWriter.Write(base.gameObject);
		return networkWriter;
	}

	[Client]
	public void FinishMessageToServer(NetworkWriter writer)
	{
		if (!NetworkClient.active)
		{
			Debug.LogWarning("[Client] function 'System.Void RoR2.NetworkUIPromptController::FinishMessageToServer(UnityEngine.Networking.NetworkWriter)' called on server");
			return;
		}
		writer.FinishMessage();
		NetworkUser networkUser = FindParticipantNetworkUser(this);
		if ((bool)networkUser)
		{
			networkUser.connectionToServer.SendWriter(writer, GetNetworkChannel());
		}
	}

	private static NetworkUser FindParticipantNetworkUser(NetworkUIPromptController instance)
	{
		if ((bool)instance)
		{
			CharacterMaster characterMaster = instance.currentParticipantMaster;
			if ((bool)characterMaster)
			{
				PlayerCharacterMasterController playerCharacterMasterController = characterMaster.playerCharacterMasterController;
				if ((bool)playerCharacterMasterController)
				{
					return playerCharacterMasterController.networkUser;
				}
			}
		}
		return null;
	}

	[NetworkMessageHandler(client = false, server = true, msgType = 76)]
	private static void HandleNetworkUIPromptMessage(NetworkMessage netMsg)
	{
		GameObject gameObject = netMsg.reader.ReadGameObject();
		if (!gameObject)
		{
			return;
		}
		NetworkUIPromptController component = gameObject.GetComponent<NetworkUIPromptController>();
		if ((bool)component)
		{
			NetworkUser networkUser = FindParticipantNetworkUser(component);
			NetworkConnection networkConnection = (networkUser ? networkUser.connectionToClient : null);
			if (netMsg.conn == networkConnection)
			{
				component.messageFromClientHandler?.Invoke(netMsg.reader);
			}
		}
	}

	private void UNetVersion()
	{
	}

	public override bool OnSerialize(NetworkWriter writer, bool forceAll)
	{
		if (forceAll)
		{
			writer.Write(masterObjectInstanceId);
			return true;
		}
		bool flag = false;
		if ((base.syncVarDirtyBits & 1) != 0)
		{
			if (!flag)
			{
				writer.WritePackedUInt32(base.syncVarDirtyBits);
				flag = true;
			}
			writer.Write(masterObjectInstanceId);
		}
		if (!flag)
		{
			writer.WritePackedUInt32(base.syncVarDirtyBits);
		}
		return flag;
	}

	public override void OnDeserialize(NetworkReader reader, bool initialState)
	{
		if (initialState)
		{
			masterObjectInstanceId = reader.ReadNetworkId();
			return;
		}
		int num = (int)reader.ReadPackedUInt32();
		if ((num & 1) != 0)
		{
			SetParticipantMasterId(reader.ReadNetworkId());
		}
	}

	public override void PreStartClient()
	{
	}
}
