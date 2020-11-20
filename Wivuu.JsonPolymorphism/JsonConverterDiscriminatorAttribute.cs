using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Wivuu.JsonPolymorphism
{
    public class JsonConverterDiscriminatorAttribute : JsonConverterAttribute
    {
        public JsonConverterDiscriminatorAttribute(Type type, string DiscriminatorProperty) : base(type)
        {
            this.DiscriminatorProperty = DiscriminatorProperty;
        }

        public JsonConverterDiscriminatorAttribute(Type type, Type DiscriminatorKind) : base(type)
        {
            this.DiscriminatorKind = DiscriminatorKind;
        }

        public string? DiscriminatorProperty { get; }
        public Type? DiscriminatorKind { get; }
    }
}
