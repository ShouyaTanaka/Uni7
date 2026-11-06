using System;

namespace Uni7.Diagnostics
{
    /// <summary>
    /// Runtime Invokerに公開するクラス、またはメソッドに付与します。
    /// クラスに付与すると対応するPublicメソッドが対象になり、
    /// メソッドに付与するとそのメソッドのみが対象になります。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public sealed class CanReflectionAttribute : Attribute
    {
        public string Category { get; }

        public CanReflectionAttribute(string category = "General")
        {
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category;
        }
    }
}
