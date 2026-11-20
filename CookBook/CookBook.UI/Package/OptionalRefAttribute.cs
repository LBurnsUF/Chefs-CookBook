using System;

namespace CookBook
{
    // view ref a prefab may leave empty
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptionalRefAttribute : Attribute { }
}
