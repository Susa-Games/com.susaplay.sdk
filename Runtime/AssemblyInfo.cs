using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("susaplay.SDK.Tests")]
// The Editor Simulator sets the Editor-only hooks on WebGLBridge and HttpClient, which stay
// internal so they never become public API.
[assembly: InternalsVisibleTo("susaplay.SDK.Editor")]
