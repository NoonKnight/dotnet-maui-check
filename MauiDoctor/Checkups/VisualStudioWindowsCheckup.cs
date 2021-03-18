﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MauiDoctor.Doctoring;
using NuGet.Versioning;

namespace MauiDoctor.Checkups
{
	public class VisualStudioWindowsCheckup : Checkup
	{
		public VisualStudioWindowsCheckup(string minimumVersion, string exactVersion = null)
		{
			MinimumVersion = NuGetVersion.Parse(minimumVersion);
			ExactVersion = exactVersion != null ? NuGetVersion.Parse(exactVersion) : null;
		}

		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.Windows;

		public NuGetVersion MinimumVersion { get; private set; } = new NuGetVersion("16.9.0");
		public NuGetVersion ExactVersion { get; private set; }

		public override string Id => "visuastudio";

		public override string Title => $"Visual Studio {MinimumVersion.ThisOrExact(ExactVersion)}";

		public override async Task<Diagonosis> Examine(PatientHistory history)
		{
			var vsinfo = await GetWindowsInfo();

			foreach (var vi in vsinfo)
			{
				if (vi.Version.IsCompatible(MinimumVersion, ExactVersion))
				{
					ReportStatus($"{vi.Version} - {vi.Path}", Status.Ok);

					var workloadResolverSentinel = Path.Combine(vi.Path, "MSBuild\\Current\\Bin\\SdkResolvers\\Microsoft.DotNet.MSBuildSdkResolver\\EnableWorkloadResolver.sentinel");

					if (Directory.Exists(workloadResolverSentinel) && !File.Exists(workloadResolverSentinel))
					{
						try
						{
							File.Create(workloadResolverSentinel);
							ReportStatus("Created EnableWorkloadResolver.sentinel for IDE support", Status.Ok);
						}
						catch { }
					}
				}
				else
					ReportStatus($"{vi.Version}", null);
			}

			if (vsinfo.Any(vs => vs.Version.IsCompatible(MinimumVersion, ExactVersion)))
				return Diagonosis.Ok(this);

			return new Diagonosis(Status.Error, this);
		}

		Task<IEnumerable<VisualStudioInfo>> GetWindowsInfo()
		{
			var items = new List<VisualStudioInfo>();

			var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				"Microsoft Visual Studio", "Installer", "vswhere.exe");


			if (!File.Exists(path))
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				"Microsoft Visual Studio", "Installer", "vswhere.exe");

			if (!File.Exists(path))
				return default;

			var r = ShellProcessRunner.Run(path,
				"-all -requires Microsoft.Component.MSBuild -format json -prerelease");

			var str = r.GetOutput();

			var json = JsonDocument.Parse(str);

			foreach (var vsjson in json.RootElement.EnumerateArray())
			{
				if (!vsjson.TryGetProperty("catalog", out var vsCat) || !vsCat.TryGetProperty("productSemanticVersion", out var vsSemVer))
					continue;

				if (!NuGetVersion.TryParse(vsSemVer.GetString(), out var semVer))
					continue;

				if (!vsjson.TryGetProperty("installationPath", out var installPath))
					continue;

				items.Add(new VisualStudioInfo
				{
					Version = semVer,
					Path = installPath.GetString()
				});
			}

			return Task.FromResult<IEnumerable<VisualStudioInfo>>(items);
		}
	}

	public struct VisualStudioInfo
	{
		public string Path { get; set; }

		public NuGetVersion Version { get; set; }
	}
}
