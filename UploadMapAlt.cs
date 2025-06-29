//Reference: System.Net.Http

using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins; 
using System.Reflection;
using System.Reflection.Emit;
using Newtonsoft.Json;
using UnityEngine;
using System.Net.Http;

namespace Oxide.Plugins
{
	[Info("Upload Map Alt", "rostov114", "0.1.0")]
	class UploadMapAlt : RustPlugin
	{
		#region Configuration
		private static Configuration _config;
		public class Configuration
		{
			[JsonProperty(PropertyName = "Upload server")]
			public string targetServer = string.Empty;
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


		#region Console Commands
		[ConsoleCommand("uma.uploadmap")]
		void CC_mc_loadweapons(ConsoleSystem.Arg arg)
		{
			BasePlayer p = arg?.Player() ?? null; 
			if (p != null && !p.IsAdmin) 
				return;

			MapUploader.IsUploaded = false;
			World.Procedural = true;
			World.Url = string.Empty;

			MapUploader.UploadMap();
		}
		#endregion

		#region HarmonyPatch
		[AutoPatch]
		[HarmonyPatch]
		class HttpRequestMessage_UploadMapImpl
		{
			static MethodBase TargetMethod()
			{
				var nestedType = typeof(MapUploader).GetNestedType("<UploadMapImpl>d__18", BindingFlags.NonPublic);
				return nestedType?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
			}

			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var codes = new List<CodeInstruction>(instructions);
				var modified = new List<CodeInstruction>();

				var nestedType = typeof(MapUploader).GetNestedType("<UploadMapImpl>d__18", BindingFlags.NonPublic);
				var requestField = nestedType?.GetField("<request>5__7", BindingFlags.NonPublic | BindingFlags.Instance);
				var getHeaders = typeof(HttpRequestMessage).GetProperty("Headers")?.GetGetMethod();
				var addHeader = typeof(System.Net.Http.Headers.HttpRequestHeaders).GetMethod("Add", new[] { typeof(string), typeof(string) });

				bool uriPatched = false;
				bool contentPatched = false;

				for (int i = 0; i < codes.Count; i++)
				{
					var code = codes[i];

					if (!uriPatched &&
						code.opcode == OpCodes.Ldstr &&
						code.operand is string s &&
						s == "https://api.facepunch.com/api/public/rust-map-upload/")
					{
						modified.Add(new CodeInstruction(OpCodes.Ldstr, _config.targetServer));
						uriPatched = true;
						continue;
					}

					modified.Add(code);

					if (!contentPatched &&
						code.opcode == OpCodes.Callvirt &&
						code.operand is MethodInfo m &&
						m.Name == "set_Content")
					{
						contentPatched = true;

						var headers = new (string, string)[]
						{
							("X-SERVER-IP", Network.Net.sv.ip),
							("X-SERVER-PORT", Network.Net.sv.port.ToString()),
							("X-SERVER-ID", (!string.IsNullOrWhiteSpace(ConVar.App.serverid) ? ConVar.App.serverid : ""))
						};

						foreach (var (key, value) in headers)
						{
							modified.Add(new CodeInstruction(OpCodes.Ldarg_0));
							modified.Add(new CodeInstruction(OpCodes.Ldfld, requestField));
							modified.Add(new CodeInstruction(OpCodes.Callvirt, getHeaders));
							modified.Add(new CodeInstruction(OpCodes.Ldstr, key));
							modified.Add(new CodeInstruction(OpCodes.Ldstr, value));
							modified.Add(new CodeInstruction(OpCodes.Callvirt, addHeader));
						}
					}
				}

				return modified;
			}
		}
		#endregion
	}
}