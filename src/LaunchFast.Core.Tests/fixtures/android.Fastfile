default_platform(:android)

# Load environment variables from a .env file in the Flutter project root.
# Defaults to .env.production; pass a different filename for other environments.
def load_env(filename = ".env.production")
  env_file = File.join(flutter_root, filename)
  UI.user_error!("#{filename} not found at #{env_file}. See .env.local.example for reference.") unless File.exist?(env_file)

  env = {}
  File.readlines(env_file).each do |line|
    line = line.strip
    next if line.empty? || line.start_with?("#")
    key, value = line.split("=", 2)
    env[key.strip] = value.strip.gsub(/\A["']|["']\z/, "") if key && value
  end

  env
end

def load_env_production
  env = load_env(".env.production")

  %w[API_URL API_TOKEN GOOGLE_WEB_CLIENT_ID].each do |var|
    UI.user_error!("#{var} not set in .env.production") if env[var].nil? || env[var].empty?
  end

  env
end

def load_env_dev
  env = load_env(".env.dev")

  %w[API_URL API_TOKEN GOOGLE_WEB_CLIENT_ID].each do |var|
    UI.user_error!("#{var} not set in .env.dev") if env[var].nil? || env[var].empty?
  end

  env
end

# Convenience: Flutter project root (two levels up from android/fastlane/)
def flutter_root
  File.expand_path("../..", Dir.pwd)
end

platform :android do

  desc "Build Flutter app bundle"
  lane :build do
    env = load_env_production

    dart_defines = [
      "API_URL=#{env['API_URL']}",
      "API_TOKEN=#{env['API_TOKEN']}",
      "GOOGLE_WEB_CLIENT_ID=#{env['GOOGLE_WEB_CLIENT_ID']}"
    ].flat_map { |d| ["--dart-define", d] }

    Dir.chdir(flutter_root) do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
      sh("flutter", "build", "appbundle", "--release", *dart_defines)
    end
  end

  desc "Deploy to Google Play internal testing track"
  lane :internal do |options|
    build
    upload_to_play_store(
      track: "internal",
      release_status: "completed",
      aab: "../build/app/outputs/bundle/release/app-release.aab",
      skip_upload_metadata: options[:skip_metadata] != false,
      skip_upload_images: options[:skip_images] != false,
      skip_upload_screenshots: options[:skip_screenshots] != false,
      # Upload the version-keyed changelogs at
      # android/fastlane/metadata/android/<locale>/changelogs/<versionCode>.txt
      # so what's-new text lands with the release and propagates when it's
      # later promoted beta -> production.
      skip_upload_changelogs: options[:skip_changelogs] == true
    )
  end

  desc "Promote internal to beta (closed testing)"
  lane :beta do
    upload_to_play_store(
      track: "internal",
      track_promote_to: "beta",
      skip_upload_metadata: true,
      skip_upload_images: true,
      skip_upload_screenshots: true
    )
  end

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

  desc "Capture Play Store screenshots using integration tests"
  lane :screenshots do |options|
    device_id = options[:device_id] || "emulator-5554"
    device_type = options[:device_type] || "phone"
    env = load_env_dev

    dart_defines = [
      "API_URL=#{env['API_URL']}",
      "API_TOKEN=#{env['API_TOKEN']}",
      "GOOGLE_WEB_CLIENT_ID=#{env['GOOGLE_WEB_CLIENT_ID']}"
    ].flat_map { |d| ["--dart-define", d] }

    Dir.chdir(flutter_root) do
      sh("flutter", "pub", "get")
      FileUtils.rm_rf("screenshots")
      FileUtils.mkdir_p("screenshots")

      sh("flutter", "drive",
         "--driver=test_driver/integration_test.dart",
         "--target=integration_test/screenshots_test.dart",
         "--device-id=#{device_id}",
         "--no-pub",
         *dart_defines)
    end

    organize_screenshots_for_play_store(device_type: device_type)
  end

  desc "Capture Play Store screenshots on both phone and tablet"
  lane :all_screenshots do |options|
    phone_id = options[:phone_id] || "emulator-5554"
    tablet_id = options[:tablet_id] || "emulator-5556"

    screenshots(device_id: phone_id, device_type: "phone")
    screenshots(device_id: tablet_id, device_type: "tablet")
  end

  desc "Capture screenshots and upload to Play Store"
  lane :screenshots_and_upload do
    screenshots
    upload_to_play_store(
      track: "internal",
      release_status: "draft",
      skip_upload_aab: true,
      skip_upload_metadata: true,
      skip_upload_images: false,
      skip_upload_screenshots: false
    )
  end

  desc "Upload store listing metadata only (no build)"
  lane :metadata do
    upload_to_play_store(
      track: "internal",
      release_status: "draft",
      version_code: 6,
      skip_upload_aab: true,
      skip_upload_apk: true,
      skip_upload_images: true,
      skip_upload_screenshots: true,
      skip_upload_changelogs: true,
      changes_not_sent_for_review: true
    )
  end

  # ---- Private helpers ----

  private_lane :organize_screenshots_for_play_store do |options|
    require 'fileutils'

    device_type = options[:device_type] || "phone"
    screenshot_source = File.join(flutter_root, "screenshots")
    metadata_base = File.join(Dir.pwd, "metadata", "android")

    folder_name = device_type == "tablet" ? "tenInchScreenshots" : "phoneScreenshots"

    locale_mapping = {
      "en" => "en-US",
      "ja" => "ja-JP"
    }

    locale_mapping.each do |lang_suffix, play_store_locale|
      dest_dir = File.join(metadata_base, play_store_locale, "images", folder_name)
      FileUtils.mkdir_p(dest_dir)

      Dir.glob(File.join(screenshot_source, "*_#{lang_suffix}.png")).sort.each do |file|
        FileUtils.cp(file, File.join(dest_dir, File.basename(file)))
        UI.success("Copied #{File.basename(file)} -> #{play_store_locale}/#{folder_name}/")
      end
    end

    UI.success("Screenshots organized in #{metadata_base}")
  end

end
