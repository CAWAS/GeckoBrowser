﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace GeckoBrowser.Properties {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "15.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("GeckoBrowser.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to 
        ///try {
        ///	if ( DMM.netgame.reloadDialog ) DMM.netgame.reloadDialog = function (){};
        ///}
        ///catch(e) {
        ///	alert(&quot;DMMによるページ更新ダイアログの非表示に失敗しました: &quot;+e);
        ///}.
        /// </summary>
        internal static string DMMScript {
            get {
                return ResourceManager.GetString("DMMScript", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to 
        ///try {{
        ///var node = document.getElementById(&apos;{0}&apos;);
        ///if (node) document.getElementsByTagName(&apos;head&apos;)[0].removeChild(node);
        ///node = document.createElement(&apos;div&apos;);
        ///node.innerHTML = &apos;F&lt;style id=\&apos;{0}\&apos;&gt;body {{ visibility: hidden; }} \
        ///#flashWrap {{ position: fixed; left: 0; top: 0; width: 100% !important; height: 100% !important; }} \
        ///#htmlWrap {{ visibility: visible; width: 100% !important; height: 100% !important; }}&lt;/style&gt;&apos;;
        ///document.getElementsByTagName(&apos;head&apos;)[0].appendChild(node.lastChild);
        ///}}
        ///ca [rest of string was truncated]&quot;;.
        /// </summary>
        internal static string FrameScript {
            get {
                return ResourceManager.GetString("FrameScript", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to 
        ///try {{
        ///var node = document.getElementById(&apos;{0}&apos;);
        ///if (node) document.getElementsByTagName(&apos;head&apos;)[0].removeChild(node);
        ///node = document.createElement(&apos;div&apos;);
        ///node.innerHTML = &apos;P&lt;style id=\&apos;{0}\&apos;&gt;body {{ visibility: hidden; overflow: hidden; }} \
        ///div #block_background {{ visibility: visible; }} \
        ///div #alert {{ visibility: visible; overflow: scroll; overflow-x: hidden; top: 3% !important; left: 3% !important; width: 94% !important; height: 94%; padding: 2%; box-sizing: border-box;}} \
        ///div.dmm-ntgnavi [rest of string was truncated]&quot;;.
        /// </summary>
        internal static string PageScript {
            get {
                return ResourceManager.GetString("PageScript", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to var node = document.getElementById(&apos;{0}&apos;);
        ///if (node) document.getElementsByTagName(&apos;head&apos;)[0].removeChild(node);.
        /// </summary>
        internal static string RestoreScript {
            get {
                return ResourceManager.GetString("RestoreScript", resourceCulture);
            }
        }
    }
}
