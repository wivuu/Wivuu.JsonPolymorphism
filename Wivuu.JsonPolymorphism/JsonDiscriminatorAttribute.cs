using System;
using System.Collections.Generic;
using System.Text;

namespace Wivuu.JsonPolymorphism
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public class JsonDiscriminatorAttribute : Attribute
    {
    }
}
