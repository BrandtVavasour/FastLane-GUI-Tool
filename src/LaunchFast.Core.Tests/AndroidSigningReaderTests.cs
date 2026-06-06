using LaunchFast.Core.Models;
using LaunchFast.Core.Signing;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class AndroidSigningReaderTests
{
    static Project ProjectAt(string root) =>
        new(
            Name: "demo",
            Path: root,
            Version: null,
            IosFastlaneDir: null,
            AndroidFastlaneDir: Path.Combine(root, "android", "fastlane"),
            HasMatchfile: false,
            IconPath: null);

    static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-androidsign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        Directory.CreateDirectory(Path.Combine(root, "android", "app"));
        return root;
    }

    const string GroovyGradle =
        """
        android {
            namespace 'com.jabtech.vmt'

            signingConfigs {
                release {
                    storeFile file(keystoreProperties['storeFile'])
                    storePassword keystoreProperties['storePassword']
                    keyAlias keystoreProperties['keyAlias']
                    keyPassword keystoreProperties['keyPassword']
                    storeType "PKCS12"
                }
            }

            buildTypes {
                release {
                    signingConfig signingConfigs.release
                    minifyEnabled true
                }
            }
        }
        """;

    const string GroovyGradleLiteral =
        """
        android {
            signingConfigs {
                release {
                    storeFile file("app/upload-keystore.jks")
                    storeType "JKS"
                    keyAlias "upload"
                }
            }
            buildTypes {
                release {
                    signingConfig signingConfigs.release
                }
            }
        }
        """;

    const string KotlinGradle =
        """
        android {
            signingConfigs {
                create("release") {
                    storeFile = file("app/release.keystore")
                    storeType = "PKCS12"
                    keyAlias = "uploadkey"
                }
            }
            buildTypes {
                getByName("release") {
                    signingConfig = signingConfigs.getByName("release")
                }
            }
        }
        """;

    const string KeyProperties =
        """
        storeFile=upload-keystore.jks
        storePassword=secret-value-should-not-be-read
        keyAlias=upload
        keyPassword=another-secret
        # a comment line
        """;

    [Test]
    public void Reads_groovy_signing_config_with_property_refs_and_key_properties()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "android", "app", "build.gradle"), GroovyGradle);
        File.WriteAllText(Path.Combine(root, "android", "key.properties"), KeyProperties);

        var info = AndroidSigningReader.Read(ProjectAt(root));

        Assert.Multiple(() =>
        {
            Assert.That(info.HasAndroid, Is.True);
            // storeFile / keyAlias reference key.properties entries → captured key names.
            Assert.That(info.StoreFile, Is.EqualTo("storeFile"));
            Assert.That(info.KeyAlias, Is.EqualTo("keyAlias"));
            Assert.That(info.StoreType, Is.EqualTo("PKCS12"));
            Assert.That(info.ReleaseSigningApplied, Is.True);
            Assert.That(info.HasKeyProperties, Is.True);
            Assert.That(info.KeyPropertyNames, Is.EquivalentTo(
                new[] { "storeFile", "storePassword", "keyAlias", "keyPassword" }));
        });
    }

    [Test]
    public void Reads_groovy_literal_store_file_and_alias()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "android", "app", "build.gradle"), GroovyGradleLiteral);

        var info = AndroidSigningReader.Read(ProjectAt(root));

        Assert.Multiple(() =>
        {
            Assert.That(info.StoreFile, Is.EqualTo("app/upload-keystore.jks"));
            Assert.That(info.StoreType, Is.EqualTo("JKS"));
            Assert.That(info.KeyAlias, Is.EqualTo("upload"));
            Assert.That(info.ReleaseSigningApplied, Is.True);
            Assert.That(info.HasKeyProperties, Is.False);
            Assert.That(info.KeyPropertyNames, Is.Empty);
        });
    }

    [Test]
    public void Reads_kotlin_dsl_signing_config()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "android", "app", "build.gradle.kts"), KotlinGradle);

        var info = AndroidSigningReader.Read(ProjectAt(root));

        Assert.Multiple(() =>
        {
            Assert.That(info.HasAndroid, Is.True);
            Assert.That(info.StoreFile, Is.EqualTo("app/release.keystore"));
            Assert.That(info.StoreType, Is.EqualTo("PKCS12"));
            Assert.That(info.KeyAlias, Is.EqualTo("uploadkey"));
            Assert.That(info.ReleaseSigningApplied, Is.True);
        });
    }

    [Test]
    public void No_signing_config_block_yields_empty_fields_but_has_android()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "android", "app", "build.gradle"),
            "android {\n    namespace 'com.example'\n}\n");

        var info = AndroidSigningReader.Read(ProjectAt(root));

        Assert.Multiple(() =>
        {
            Assert.That(info.HasAndroid, Is.True);
            Assert.That(info.StoreFile, Is.Null);
            Assert.That(info.StoreType, Is.Null);
            Assert.That(info.KeyAlias, Is.Null);
            Assert.That(info.ReleaseSigningApplied, Is.False);
            Assert.That(info.HasKeyProperties, Is.False);
        });
    }

    [Test]
    public void No_android_module_yields_none()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-noandroid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = new Project("demo", root, null, null, null, false, null);

        var info = AndroidSigningReader.Read(project);

        Assert.Multiple(() =>
        {
            Assert.That(info.HasAndroid, Is.False);
            Assert.That(info, Is.EqualTo(AndroidSigningInfo.None));
        });
    }

    [Test]
    public void Signing_config_present_but_not_applied_to_release_build_type()
    {
        var root = NewRoot();
        File.WriteAllText(Path.Combine(root, "android", "app", "build.gradle"),
            """
            android {
                signingConfigs {
                    release {
                        storeType "PKCS12"
                        keyAlias "upload"
                    }
                }
                buildTypes {
                    release {
                        minifyEnabled true
                    }
                }
            }
            """);

        var info = AndroidSigningReader.Read(ProjectAt(root));

        Assert.Multiple(() =>
        {
            Assert.That(info.KeyAlias, Is.EqualTo("upload"));
            Assert.That(info.ReleaseSigningApplied, Is.False);
        });
    }
}
