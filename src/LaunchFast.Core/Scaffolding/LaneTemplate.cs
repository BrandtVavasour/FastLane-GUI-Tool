using LaunchFast.Core.Models;

namespace LaunchFast.Core.Scaffolding;

/// <summary>
/// Registry of per-lane Ruby templates, modelled on the proven VendingMachine
/// Fastfiles. <see cref="Render"/> returns the Ruby for ONE lane (a <c>desc</c>
/// line plus its <c>lane :name do … end</c> block), indented to sit inside a
/// <c>platform</c> block.
/// </summary>
public static class LaneTemplate
{
    public static IReadOnlyList<string> Available(Platform p) => p == Platform.Ios
        ? ["sync_certificates", "beta", "release", "screenshots"]
        : ["build", "internal", "beta", "production"];

    public static string Render(Platform platform, string lane, WizardAnswers a) =>
        platform == Platform.Ios ? RenderIos(lane, a) : RenderAndroid(lane, a);

    /// <summary>
    /// Renders the dart-define args as their own indented lines inside an
    /// <c>sh("flutter", "build", …)</c> call. Each becomes
    /// <c>"--dart-define=NAME=#{ENV['ENVVAR']}",</c> with a trailing comma so the
    /// caller can follow it with the next positional arg on its own line.
    /// </summary>
    static string IosDartDefineLines(WizardAnswers a, string indent) =>
        string.Concat(a.DartDefines.Select(kv =>
            $"{indent}\"--dart-define={kv.Key}=#{{ENV['{kv.Value}']}}\",\n"));

    /// <summary>
    /// Renders the dart-define args as a trailing, comma-separated arg list for an
    /// <c>sh("flutter", "build", "appbundle", "--release", &lt;defines&gt;)</c> call.
    /// Produces a leading <c>, </c> for each entry so it appends cleanly after
    /// <c>"--release"</c>; never a trailing comma.
    /// </summary>
    static string AndroidDartDefineArgs(WizardAnswers a) =>
        string.Concat(a.DartDefines.Select(kv =>
            $", \"--dart-define={kv.Key}=#{{ENV['{kv.Value}']}}\""));

    static string RenderIos(string lane, WizardAnswers a) => lane switch
    {
        "sync_certificates" =>
"""
  desc "Sync code signing certificates"
  lane :sync_certificates do
    match(type: "appstore", readonly: is_ci, git_url: ENV["MATCH_GIT_URL"])
  end
""",
        "beta" =>
$$"""
  desc "Build and upload to TestFlight"
  lane :beta do
    sync_certificates
    export_options_path = File.expand_path("../ExportOptions.plist", __dir__)
    Dir.chdir("..") do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
      sh(
        "flutter", "build", "ipa",
        "--release",
{{IosDartDefineLines(a, "        ")}}        "--export-options-plist=#{export_options_path}"
      )
    end
    upload_to_testflight(
      ipa: "../build/ios/ipa/#{ENV['IPA_NAME'] || 'app'}.ipa",
      skip_waiting_for_build_processing: true,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"]
    )
  end
""",
        "release" =>
$$"""
  desc "Build and upload to App Store"
  lane :release do
    sync_certificates
    export_options_path = File.expand_path("../ExportOptions.plist", __dir__)
    Dir.chdir("..") do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
      sh(
        "flutter", "build", "ipa",
        "--release",
{{IosDartDefineLines(a, "        ")}}        "--export-options-plist=#{export_options_path}"
      )
    end
    upload_to_app_store(
      ipa: "../build/ios/ipa/#{ENV['IPA_NAME'] || 'app'}.ipa",
      submit_for_review: false,
      automatic_release: false,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"]
    )
  end
""",
        "screenshots" =>
"""
  desc "Capture screenshots for App Store"
  lane :screenshots do
    capture_screenshots
    upload_to_app_store(
      skip_binary_upload: true,
      skip_metadata: true,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"]
    )
  end
""",
        _ => throw new ArgumentException($"Unknown iOS lane '{lane}'", nameof(lane))
    };

    static string RenderAndroid(string lane, WizardAnswers a) => lane switch
    {
        "build" =>
$$"""
  desc "Build Flutter app bundle"
  lane :build do
    Dir.chdir(flutter_root) do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
      sh("flutter", "build", "appbundle", "--release"{{AndroidDartDefineArgs(a)}})
    end
  end
""",
        "internal" =>
"""
  desc "Deploy to Google Play internal testing track"
  lane :internal do
    build
    upload_to_play_store(
      track: "internal",
      release_status: "completed",
      aab: "../build/app/outputs/bundle/release/app-release.aab"
    )
  end
""",
        "beta" =>
"""
  desc "Promote internal to beta"
  lane :beta do
    upload_to_play_store(
      track: "internal",
      track_promote_to: "beta",
      skip_upload_metadata: true,
      skip_upload_images: true,
      skip_upload_screenshots: true
    )
  end
""",
        "production" =>
"""
  desc "Promote beta to production"
  lane :production do
    upload_to_play_store(
      track: "beta",
      track_promote_to: "production",
      skip_upload_metadata: true,
      skip_upload_images: true,
      skip_upload_screenshots: true
    )
  end
""",
        _ => throw new ArgumentException($"Unknown Android lane '{lane}'", nameof(lane))
    };
}
