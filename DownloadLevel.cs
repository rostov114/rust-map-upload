using UnityEngine;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using ProtoBuf;
using Network;

using HarmonyLib;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;

using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace Oxide.Plugins
{
	[Info("Download Level", "rostov114", "0.3.1")]
	class DownloadLevel : RustPlugin
	{
		private static DownloadLevel _instance;
		private Dictionary<string, string> reqHeaders = new();
		private List<string> alternativeServersWorked = new();
		private Dictionary<ulong, List<string>> _attempt = new();
		private List<ulong> _pending = new();

		#region Init
		private void Init()
		{
			_instance = this;

   			reqHeaders.Add("X-SERVER-IP", Network.Net.sv.ip);
			reqHeaders.Add("X-SERVER-PORT", Network.Net.sv.port.ToString());
			reqHeaders.Add("X-SERVER-ID", (!string.IsNullOrWhiteSpace(ConVar.App.serverid) ? ConVar.App.serverid : ""));
		}

		private void OnServerInitialized(bool serverInitialized)
		{
			if (!serverInitialized)
			{
				MapUploader_UploadMapImpl.InternalCallback(World.Url);
			}
		}
		#endregion

		#region Configuration
		private Configuration _config;
		public class Configuration
		{
			[JsonProperty(PropertyName = "Primary map server")]
			public string primaryServer = string.Empty;

			[JsonProperty(PropertyName = "Preupload map script")]
			public string preuploadScript = "preuploadmap.php";

			[JsonProperty(PropertyName = "Alternative map servers")]
			public List<string> alternativeServers = new List<string>();
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				_config = Config.ReadObject<Configuration>();
			}
			catch
			{
				PrintError("Error reading config, please check!");
			}
		}

		protected override void LoadDefaultConfig()
		{
			_config = new Configuration();
			SaveConfig();
		}

		protected override void SaveConfig() => Config.WriteObject(_config);
		#endregion

		#region HarmonyPatch
		[AutoPatch]
		[HarmonyPatch(typeof(MapUploader), "UploadMapImpl")]
		public static class MapUploader_UploadMapImpl
		{
			public static void Postfix(Task<string> __result)
			{
				__result.ContinueWith(task =>
				{
					if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
					{
						InternalCallback(task.Result);
					}
				});
			}

			public static void InternalCallback(string url)
			{
				if (!string.IsNullOrEmpty(_instance._config.preuploadScript))
				{
					foreach (string server in _instance._config.alternativeServers)
					{
						_instance.webrequest.Enqueue(server + _instance._config.preuploadScript, url, (code, response) =>
						{
							switch (code)
							{
								case 0:
									_instance.PrintError($"Time out waiting for API");
									break; 
							}

							_instance.Puts($"{server}: {response}");
							_instance.alternativeServersWorked.Add(server);
						}, 
						_instance, 
						RequestMethod.POST,
						_instance.reqHeaders);
					}
				}
			}
		}

		[AutoPatch]
		[HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.JoinGame))]
		class ServerMgr_JoinGame
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var codes = new List<CodeInstruction>(instructions);
				var targetMethod = AccessTools.Method(typeof(Approval), "WriteToStream");
				for (int i = 0; i < codes.Count; i++)
				{
					if (codes[i].Calls(targetMethod))
					{
						codes.Insert(i,     new CodeInstruction(OpCodes.Ldloc_0));
						codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
						codes.Insert(i + 2, new CodeInstruction(OpCodes.Call,
							AccessTools.Method(typeof(ServerMgr_JoinGame), nameof(InternalCallback))));

						break;
					}
				}

				return codes;
			}

			public static void InternalCallback(Approval approval, Network.Connection connection)
			{
				if (_instance._pending.Contains(connection.userid) && _instance.alternativeServersWorked.Count > 0)
				{
					_instance._pending.Remove(connection.userid);

					if (!_instance._attempt.ContainsKey(connection.userid))
						_instance._attempt.Add(connection.userid, new List<string>());

					foreach (string server in _instance.alternativeServersWorked)
					{
						if (!_instance._attempt[connection.userid].Contains(server))
						{
							approval.levelUrl = (string.IsNullOrEmpty(_instance._config.primaryServer)) ? server : World.Url.Replace(_instance._config.primaryServer, server);
							_instance._attempt[connection.userid].Add(server);

							_instance.PrintWarning($"{connection} using alternative map server: {approval.levelUrl}");
							return;
						}
					}

					_instance.PrintWarning($"{connection} reset all alternative servers and using primary server!");
					_instance._attempt.Remove(connection.userid);
				}
			}
		}
		#endregion

		#region Oxide Hooks
		private void OnPlayerConnected(BasePlayer player)
		{
			_attempt.Remove(player.userID);
		}

		private void OnClientDisconnect(Connection connection, string reason)
		{
			if (connection == null || reason == null || alternativeServersWorked.Count == 0)
				return;

			if (reason.StartsWith("Couldn't Download Level"))
				_pending.Add(connection.userid);

			if (reason == "disconnect" || reason == "closing")
				_attempt.Remove(connection.userid);
		}

		private void OnClientDisconnected(Connection connection, string reason)
		{
			if (reason == "Timed Out")
				_attempt.Remove(connection.userid);
		}
		#endregion
	}
}
