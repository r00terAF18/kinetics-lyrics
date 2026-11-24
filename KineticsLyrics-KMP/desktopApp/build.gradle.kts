import org.jetbrains.compose.desktop.application.dsl.TargetFormat

plugins {
    alias(libs.plugins.kotlinMultiplatform)
    alias(libs.plugins.composeMultiplatform)
    alias(libs.plugins.composeCompiler)
}

kotlin {
    jvm {
        compilations.all {
            kotlinOptions {
                jvmTarget = "21"
            }
        }
    }

    sourceSets {
        val jvmMain by getting {
            dependencies {
                implementation(project(":shared"))
                implementation(compose.desktop.currentOs)
                implementation(libs.kotlinx.coroutines.swing)
                implementation(libs.kotlinx.serialization.json)
            }
        }
    }
}

compose.desktop {
    application {
        mainClass = "com.kineticlyrics.desktop.MainKt"

        nativeDistributions {
            targetFormats(TargetFormat.Msi, TargetFormat.Exe, TargetFormat.Dmg)
            packageName = "KineticLyricsWallpaper"
            packageVersion = "1.0.0"

            windows {
                menuGroup = "Kinetic Lyrics"
                upgradeUuid = "8d9c4f1a-5b2e-4f8a-9c3d-1e7f6a2b4c8d"
            }
            macOS {
                bundleID = "com.kineticlyrics.wallpaper"
            }
        }
    }
}
