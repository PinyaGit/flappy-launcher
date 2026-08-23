//------------------------------------------------------------------------------
// Embedded UI assets. Edit Resources/*.png and Assets/*.png, then rebuild.
// Naming: bg_* splash, logo_* rail icons — by product branch / version.
//------------------------------------------------------------------------------

namespace FlappyReDovahLauncher.Properties {
    using System;

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {

        private static global::System.Resources.ResourceManager resourceMan;
        private static global::System.Globalization.CultureInfo resourceCulture;

        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }

        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    resourceMan = new global::System.Resources.ResourceManager(
                        "FlappyReDovahLauncher.Properties.Resources", typeof(Resources).Assembly);
                }
                return resourceMan;
            }
        }

        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get { return resourceCulture; }
            set { resourceCulture = value; }
        }

        /// <summary>Re-Dovah splash / background.</summary>
        internal static System.Drawing.Bitmap bg_re_dovah {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("bg_re_dovah", resourceCulture); }
        }

        /// <summary>Flappy 4.0.0 splash / poster.</summary>
        internal static System.Drawing.Bitmap bg_flappy_400 {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("bg_flappy_400", resourceCulture); }
        }

        /// <summary>Re-Dovah rail logo.</summary>
        internal static System.Drawing.Bitmap logo_re_dovah {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("logo_re_dovah", resourceCulture); }
        }

        /// <summary>Flappy 4.0.0 rail logo (shared RU/EN).</summary>
        internal static System.Drawing.Bitmap logo_flappy_400 {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("logo_flappy_400", resourceCulture); }
        }

        internal static System.Drawing.Bitmap discord {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("discord", resourceCulture); }
        }

        internal static System.Drawing.Bitmap boosty {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("boosty", resourceCulture); }
        }

        internal static System.Drawing.Bitmap edge_bar_fill {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("edge_bar_fill", resourceCulture); }
        }

        internal static System.Drawing.Bitmap edge_bar_frame {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("edge_bar_frame", resourceCulture); }
        }

        internal static System.Drawing.Bitmap edge_bar_track {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("edge_bar_track", resourceCulture); }
        }

        internal static System.Drawing.Bitmap cross {
            get { return (System.Drawing.Bitmap)ResourceManager.GetObject("cross", resourceCulture); }
        }
    }
}
