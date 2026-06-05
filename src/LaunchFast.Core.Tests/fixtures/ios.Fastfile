require 'dotenv'

default_platform(:ios)

platform :ios do
  before_all do
    setup_ci if ENV['CI']

    # Load environment file (default to .env.production)
    env_file = ENV['FASTLANE_ENV'] || '.env.production'
    env_path = File.join(flutter_root, env_file)
    Dotenv.load(env_path) if File.exist?(env_path)
  end

  desc "Sync code signing certificates"
  lane :sync_certificates do
    match(
      type: "appstore",
      readonly: is_ci,
      git_url: ENV["MATCH_GIT_URL"],
      keychain_name: ENV["MATCH_KEYCHAIN_NAME"],
      keychain_password: ENV["MATCH_KEYCHAIN_PASSWORD"]
    )
  end

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
        "--dart-define=API_URL=#{ENV['API_URL']}",
        "--dart-define=API_TOKEN=#{ENV['API_TOKEN']}",
        "--export-options-plist=#{export_options_path}"
      )
    end

    upload_to_testflight(
      ipa: "../build/ios/ipa/vending_machine_tracker.ipa",
      skip_waiting_for_build_processing: true,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"]
    )
  end

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
        "--dart-define=API_URL=#{ENV['API_URL']}",
        "--dart-define=API_TOKEN=#{ENV['API_TOKEN']}",
        "--export-options-plist=#{export_options_path}"
      )
    end

    upload_to_app_store(
      ipa: "../build/ios/ipa/vending_machine_tracker.ipa",
      submit_for_review: false,
      automatic_release: false,
      skip_screenshots: false,
      skip_metadata: false,
      overwrite_screenshots: true,
      force: true,
      run_precheck_before_submit: false,
      precheck_include_in_app_purchases: false,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"]
    )
  end

  desc "Capture screenshots for App Store"
  lane :screenshots do
    # Load dev environment for screenshots, OVERRIDING anything set by
    # before_all (which loaded .env.production). Dotenv.overload replaces
    # already-set values; Dotenv.load does not.
    dev_env = File.join(flutter_root, ".env.dev")
    Dotenv.overload(dev_env) if File.exist?(dev_env)

    if ENV['SCREENSHOT_USERNAME'].nil? || ENV['SCREENSHOT_PASSWORD'].nil?
      UI.user_error!("SCREENSHOT_USERNAME and SCREENSHOT_PASSWORD must be set (in .env.dev) so the screenshot tests can log in to the dev API.")
    end

    # Clean and prepare
    Dir.chdir("..") do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
    end

    # Capture screenshots for each device type and language.
    # Apple requires a 6.9" iPhone and a 13" iPad set per locale; iPhone 13 mini
    # is not in Apple's required list, so it's omitted (and the simulator isn't
    # installed locally).
    devices = [
      { name: "iPhone 17 Pro Max", type: "phone", lang: "en" },
      { name: "iPhone 17 Pro Max", type: "phone", lang: "ja" },
      { name: "iPad Pro 13-inch (M5)", type: "tablet", lang: "en" },
      { name: "iPad Pro 13-inch (M5)", type: "tablet", lang: "ja" }
    ]

    devices.each do |device|
      capture_screenshots_for_device(
        device: device[:name],
        type: device[:type],
        language: device[:lang]
      )
    end

    # Organize screenshots for App Store
    organize_screenshots_for_app_store
  end

  # Private lane: Capture for specific device via xcodebuild test against
  # the RunnerTests Unit Testing Bundle (which hosts INTEGRATION_TEST_IOS_RUNNER
  # in ios/RunnerTests/IntegrationTests.m). Screenshots are auto-attached to
  # the .xcresult bundle by the macro; we extract them with xcresulttool.
  private_lane :capture_screenshots_for_device do |options|
    device_name = options[:device]
    language = options[:language]

    device_id = sh("xcrun simctl list devices available | grep '#{device_name}' | head -1 | grep -o '[0-9A-F-]\\{36\\}'").strip
    if device_id.empty?
      UI.user_error!("Simulator not installed: '#{device_name}'. Install via Xcode > Settings > Platforms or pick a different device in the Fastfile.")
    end

    UI.message("Capturing screenshots for #{device_name} (#{language})…")

    raw_output = File.expand_path(File.join(flutter_root, "screenshots"))
    FileUtils.mkdir_p(raw_output)

    # Pre-grant runtime permissions so iOS system dialogs don't appear over
    # the app and shadow the screenshots. `|| true` is intentional — a fresh
    # simulator with no privacy DB returns non-zero on first grant.
    sh("xcrun simctl privacy #{device_id} grant location-always au.com.jabtech.vendingMachineTracker || true")
    sh("xcrun simctl privacy #{device_id} grant camera au.com.jabtech.vendingMachineTracker || true")
    sh("xcrun simctl privacy #{device_id} grant photos au.com.jabtech.vendingMachineTracker || true")

    Dir.chdir("..") do
      # 1. Make Flutter compile our integration test as the Runner.app's
      # entry point. `--config-only` writes the entry-point setting into the
      # Xcode build configuration without actually building.
      ENV['FLUTTER_LOCALE'] = language
      sh(
        "flutter", "build", "ios",
        "--config-only",
        "--simulator",
        "--target=integration_test/screenshots_test.dart",
        "--dart-define=API_URL=#{ENV['API_URL']}",
        "--dart-define=API_TOKEN=#{ENV['API_TOKEN']}",
        "--dart-define=SCREENSHOT_USERNAME=#{ENV['SCREENSHOT_USERNAME']}",
        "--dart-define=SCREENSHOT_PASSWORD=#{ENV['SCREENSHOT_PASSWORD']}",
        "--dart-define=FLUTTER_LOCALE=#{language}"
      )
    end

    # 2. Run the integration tests via xcodebuild test. The result bundle is
    # written to a per-run path so successive devices/locales don't clobber.
    result_bundle = "/tmp/screenshots_#{device_id}_#{language}.xcresult"
    FileUtils.rm_rf(result_bundle)

    ios_dir = File.join(flutter_root, "ios")
    Dir.chdir(ios_dir) do
      sh(
        "xcodebuild", "test",
        "-workspace", "Runner.xcworkspace",
        "-scheme", "Runner",
        "-configuration", "Debug",
        "-destination", "platform=iOS Simulator,id=#{device_id}",
        "-only-testing:RunnerTests/IntegrationTests",
        "-resultBundlePath", result_bundle,
        "-quiet"
      )
    end

    # 3. Extract every PNG attachment from the .xcresult bundle and rename
    # them with the device prefix so iPhone vs iPad sets coexist.
    extract_attachments_from_xcresult(
      xcresult_path: result_bundle,
      output_dir: raw_output,
      filename_prefix: "#{device_name}-"
    )
  end

  # Walks an .xcresult bundle, finds every XCTAttachment whose name looks
  # like `<screenshot-name>_<index>_<UUID>.png` (the form
  # `INTEGRATION_TEST_IOS_RUNNER` produces from `binding.takeScreenshot`),
  # and writes the raw PNG payloads to `output_dir` with the original
  # `<screenshot-name>.png` plus the device prefix.
  #
  # Xcode 16's xcresulttool deprecated the old format; we use the new
  # `get test-results activities` to enumerate attachments and
  # `export object --legacy` (the only export form that still exists) to
  # pull each payload by its `payloadId`.
  private_lane :extract_attachments_from_xcresult do |options|
    require 'json'

    xcresult = options[:xcresult_path]
    output_dir = options[:output_dir]
    prefix = options[:filename_prefix] || ""
    test_id = options[:test_id] || "IntegrationTests/screenshotPlaceholder"

    UI.message("Extracting screenshots from #{xcresult} → #{output_dir}")
    FileUtils.mkdir_p(output_dir)

    json_str = `xcrun xcresulttool get test-results activities --path "#{xcresult}" --test-id "#{test_id}" 2>/dev/null`
    if json_str.empty?
      UI.user_error!("xcresulttool returned no output for #{xcresult} (test-id: #{test_id})")
    end
    root = JSON.parse(json_str)

    # Walk the activity tree; every node may carry an `attachments` array.
    attachments = []
    walker = lambda do |node|
      case node
      when Hash
        if node["attachments"].is_a?(Array)
          node["attachments"].each do |att|
            name = att["name"]
            payload = att["payloadId"]
            if name && payload && name.downcase.end_with?(".png")
              attachments << { name: name, payload: payload }
            end
          end
        end
        node.each_value { |v| walker.call(v) }
      when Array
        node.each { |v| walker.call(v) }
      end
    end
    walker.call(root)

    UI.message("Found #{attachments.size} screenshot attachment(s)")
    if attachments.empty?
      UI.user_error!("No PNG attachments in #{xcresult} — the macro probably didn't fire. Check that IntegrationTests.m is compiled into RunnerTests.")
    end

    attachments.each do |att|
      # Xcode appends `_<run-index>_<UUID>.png` to the original name passed to
      # `binding.takeScreenshot('01_login_en')`. Strip both to get the logical
      # name for the App Store listing.
      logical = att[:name]
        .sub(/\.png$/i, "")
        .sub(/_\d+_[0-9A-F-]{36}$/i, "")

      final = File.join(output_dir, "#{prefix}#{logical}.png")
      sh("xcrun xcresulttool export object --legacy --type file --id '#{att[:payload]}' --path '#{xcresult}' --output-path '#{final}' >/dev/null 2>&1")
      unless File.exist?(final)
        UI.error("xcresulttool didn't produce payload for #{att[:name]}")
        next
      end

      # App Store Connect rejects PNGs with an alpha channel
      # (IMAGE_ALPHA_NOT_ALLOWED). iOS screenshots are RGBA by default, so
      # round-trip through JPEG via `sips` to flatten alpha while keeping
      # the file as a PNG.
      sh("sips -s format jpeg '#{final}' --out '#{final}.jpg' >/dev/null 2>&1 && sips -s format png '#{final}.jpg' --out '#{final}' >/dev/null 2>&1 && rm '#{final}.jpg'")
    end
  end

  # Private lane: Organize screenshots into App Store structure
  private_lane :organize_screenshots_for_app_store do
    screenshots_dir = File.join(flutter_root, "screenshots")
    # Use an absolute path. fastlane's CWD inside private lanes is the
    # directory containing the Fastfile (ios/fastlane), so a relative
    # "./fastlane/screenshots" lands in ios/fastlane/fastlane/screenshots.
    output_dir = File.expand_path("screenshots", __dir__)

    # Create locale directories. App Store Connect's locale code for
    # Japanese is "ja" (not "ja-JP") — deliver rejects "ja-JP" as invalid.
    FileUtils.mkdir_p("#{output_dir}/en-US")
    FileUtils.mkdir_p("#{output_dir}/ja")

    # Map language codes
    lang_map = { "en" => "en-US", "ja" => "ja" }

    # Copy and organize screenshots
    Dir.glob("#{screenshots_dir}/*.png").each do |file|
      filename = File.basename(file)

      # Extract language from filename (e.g., "01_login_en.png" → "en")
      if filename =~ /_([a-z]{2})\.png$/
        lang = $1
        locale = lang_map[lang]

        if locale
          FileUtils.cp(file, "#{output_dir}/#{locale}/#{filename}")
        end
      end
    end

    UI.success("Screenshots organized for App Store")
  end
end

# Helper function to get Flutter project root
# __dir__ is ios/fastlane/, so go up two levels to reach project root
def flutter_root
  File.expand_path('../..', __dir__)
end
