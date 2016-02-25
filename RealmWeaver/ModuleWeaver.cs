/* Copyright 2015 Realm Inc - All Rights Reserved
 * Proprietary and Confidential
 */
 
using System;
using System.Collections;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

public class ModuleWeaver
{
    // Will log an informational message to MSBuild
    public Action<string> LogInfo { get; set; }

    public Action<string, SequencePoint> LogWarningPoint { get; set; }

    public Action<string, SequencePoint> LogErrorPoint { get; set; }

    // An instance of Mono.Cecil.ModuleDefinition for processing
    public ModuleDefinition ModuleDefinition { get; set; }

    TypeSystem typeSystem;

    MethodReference realmObjectIsManagedGetter;

    // Init logging delegates to make testing easier
    public ModuleWeaver()
    {
        LogInfo = m => { };
        LogWarningPoint = (m, p) => { };
        LogErrorPoint = (m, p) => { };
    }

    IEnumerable<TypeDefinition> GetMatchingTypes()
    {
        return ModuleDefinition.GetTypes().Where(x => (x.BaseType != null && x.BaseType.Name == "RealmObject"));
    }

    bool IsRealmObject(TypeReference prop)
    {
        string leafClassName = prop.Name;
        // TODO make smart enough to cope with subclasses of classes descending from RealmObject
        // for now is good enough to cope with only direct subclasses
        var matches = ModuleDefinition.GetTypes().Where(x => (x.BaseType != null && x.BaseType.Name == "RealmObject" && x.Name == leafClassName));
        return matches.Count() == 1;
    }


    internal MethodReference MethodNamed(TypeDefinition assemblyType, string name)
    {
        try
        {
            return ModuleDefinition.Import(assemblyType.Methods.First(x => x.Name == name));
        }
        catch (InvalidOperationException e)
        {
            throw new InvalidOperationException("Trying to find method '" + name + "' failed.", e);
        }
    }


    public void Execute()
    {
        // UNCOMMENT THIS DEBUGGER LAUNCH TO BE ABLE TO RUN A SEPARATE VS INSTANCE TO DEBUG WEAVING WHILST BUILDING
        // Debugger.Launch();  

        typeSystem = ModuleDefinition.TypeSystem;

        var assemblyToReference = ModuleDefinition.AssemblyResolver.Resolve("Realm");  // Note that the assembly is Realm but the namespace Realms with the s

        var realmObjectType = assemblyToReference.MainModule.GetTypes().First(x => x.Name == "RealmObject");
        realmObjectIsManagedGetter = ModuleDefinition.ImportReference(realmObjectType.Properties.Single(x => x.Name == "IsManaged").GetMethod);
        
        var typeTable = new Dictionary<string, string>()
        {
            {"System.String", "String"},
            {"System.Char", "Char"},
            {"System.Byte", "Byte"},
            {"System.Int16", "Int16"},
            {"System.Int32", "Int32"},
            {"System.Int64", "Int64"},
            {"System.Single", "Single"},
            {"System.Double", "Double"},
            {"System.Boolean", "Boolean"},
            {"System.DateTimeOffset", "DateTimeOffset"},
            {"System.Nullable`1<System.Char>", "NullableChar"},
            {"System.Nullable`1<System.Byte>", "NullableByte"},
            {"System.Nullable`1<System.Int16>", "NullableInt16"},
            {"System.Nullable`1<System.Int32>", "NullableInt32"},
            {"System.Nullable`1<System.Int64>", "NullableInt64"},
            {"System.Nullable`1<System.Single>", "NullableSingle"},
            {"System.Nullable`1<System.Double>", "NullableDouble"},
            {"System.Nullable`1<System.Boolean>", "NullableBoolean"},
        };

        // Cache of getter and setter methods for the various types.
        var methodTable = new Dictionary<string, Tuple<MethodReference, MethodReference>>();

        var objectIdTypes = new List<string>
        {
            "System.String",
            "System.Char",
            "System.Byte",
            "System.Int16",
            "System.Int32",
            "System.Int64",
        };

        var genericGetObjectValueReference = MethodNamed(realmObjectType, "GetObjectValue");
        var genericSetObjectValueReference = MethodNamed(realmObjectType, "SetObjectValue");
        var genericGetListValueReference = MethodNamed(realmObjectType, "GetListValue");

        var wovenAttributeClass = assemblyToReference.MainModule.GetTypes().First(x => x.Name == "WovenAttribute");
        var wovenAttributeConstructor = ModuleDefinition.Import(wovenAttributeClass.GetConstructors().First());

        var wovenPropertyAttributeClass = assemblyToReference.MainModule.GetTypes().First(x => x.Name == "WovenPropertyAttribute");
        var wovenPropertyAttributeConstructor = ModuleDefinition.ImportReference(wovenPropertyAttributeClass.GetConstructors().First());
        var corlib = ModuleDefinition.AssemblyResolver.Resolve((AssemblyNameReference)ModuleDefinition.TypeSystem.CoreLibrary);
        var stringType = ModuleDefinition.ImportReference(corlib.MainModule.GetType("System.String"));
        // WARNING the GetType("System.Collections.Generic.List`1") below RETURNS NULL WHEN COMPILING A PCL
        // UNUSED SO COMMENT OUT var listType = ModuleDefinition.ImportReference(corlib.MainModule.GetType("System.Collections.Generic.List`1"));

        foreach (var type in GetMatchingTypes())
        {
            if (type == null) {
                Debug.WriteLine("Weaving skipping null type from GetMatchingTypes");
                continue;
            }
            Debug.WriteLine("Weaving " + type.Name);
            var typeHasObjectId = false;

            foreach (var prop in type.Properties.Where(x => !x.CustomAttributes.Any(a => a.AttributeType.Name == "IgnoredAttribute")))
            {
                var sequencePoint = prop.GetMethod.Body.Instructions.First().SequencePoint;

                var columnName = prop.Name;
                var mapToAttribute = prop.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "MapToAttribute");
                if (mapToAttribute != null)
                    columnName = ((string)mapToAttribute.ConstructorArguments[0].Value);

                var backingField = GetBackingField(prop);

                Debug.Write("  - " + prop.PropertyType.FullName + " " + prop.Name + " (Column: " + columnName + ").. ");

                var objectId = prop.CustomAttributes.Any(a => a.AttributeType.Name == "ObjectIdAttribute");
                if (objectId && (!objectIdTypes.Contains(prop.PropertyType.FullName)))
                {
                    LogErrorPoint($"{type.Name}.{prop.Name} is marked as [ObjectId] which is only allowed on integral and string types, not on {prop.PropertyType.FullName}", sequencePoint);
                    continue;
                }

                if (!prop.IsAutomatic())
                {
                    if (IsRealmObject(prop.PropertyType))
                        LogWarningPoint($"{type.Name}.{columnName} is not an automatic property but its type is a RealmObject which normally indicates a relationship", sequencePoint);

                    Debug.WriteLine("Skipped because it's not automatic.");
                    continue;
                }
                if (typeTable.ContainsKey(prop.PropertyType.FullName))
                {
                    var typeId = prop.PropertyType.FullName + (objectId ? " unique" : "");
                    if (!methodTable.ContainsKey(typeId))
                    {
                        var getter = MethodNamed(realmObjectType, "Get" + typeTable[prop.PropertyType.FullName] + "Value");
                        var setter = MethodNamed(realmObjectType, "Set" + typeTable[prop.PropertyType.FullName] + "Value" + (objectId ? "Unique": ""));
                        methodTable[typeId] = Tuple.Create(getter, setter);
                    }

                    ReplaceGetter(prop, columnName, methodTable[typeId].Item1);
                    ReplaceSetter(prop, columnName, methodTable[typeId].Item2);
                }
//                else if (prop.PropertyType.Name == "IList`1" && prop.PropertyType.Namespace == "System.Collections.Generic")
                else if (prop.PropertyType.Name == "RealmList`1" && prop.PropertyType.Namespace == "Realms")
                {
                    // RealmList allows people to declare lists only of RealmObject due to the class definition
                    if (!prop.IsAutomatic())
                    {
                        LogWarningPoint($"{type.Name}.{columnName} is not an automatic property but its type is a RealmList which normally indicates a relationship", sequencePoint);
                        continue;
                    }
                    if (prop.SetMethod != null)
                    {
                        LogWarningPoint($"{type.Name}.{columnName} has a setter but its type is a RealmList which only supports getters", sequencePoint);
                        continue;
                    }

                    var elementType = ((GenericInstanceType)prop.PropertyType).GenericArguments.Single();
                    ReplaceGetter(prop, columnName, new GenericInstanceMethod(genericGetListValueReference) { GenericArguments = { elementType } });
                }
                else if (IsRealmObject(prop.PropertyType))
                {
                    if (!prop.IsAutomatic())
                    {
                        LogWarningPoint($"{type.Name}.{columnName} is not an automatic property but its type is a RealmObject which normally indicates a relationship", sequencePoint);
                        continue;
                    }

                    ReplaceGetter(prop, columnName, new GenericInstanceMethod(genericGetObjectValueReference) { GenericArguments = { prop.PropertyType } });
                    ReplaceSetter(prop, columnName, new GenericInstanceMethod(genericSetObjectValueReference) { GenericArguments = { prop.PropertyType } });  // with casting in the RealmObject methods, should just work
                }
                else if (prop.PropertyType.FullName == "System.DateTime")
                {
                    LogErrorPoint($"class '{type.Name}' field '{prop.Name}' is a DateTime which is not supported - use DateTimeOffset instead.", sequencePoint);
                }
                else
                {
                    LogErrorPoint($"class '{type.Name}' field '{columnName}' is a '{prop.PropertyType}' which is not yet supported", sequencePoint);
                }

                var wovenPropertyAttribute = new CustomAttribute(wovenPropertyAttributeConstructor);
                wovenPropertyAttribute.ConstructorArguments.Add(new CustomAttributeArgument(stringType, backingField.Name));
                prop.CustomAttributes.Add(wovenPropertyAttribute);

                Debug.WriteLine("");
            }

            type.CustomAttributes.Add(new CustomAttribute(wovenAttributeConstructor));
            Debug.WriteLine("");
        }

        return;
    }

    void PrependListFieldInitializerToConstructor(FieldReference field, MethodDefinition constructor, MethodReference listConstructor)
    {
        var start = constructor.Body.Instructions.First();
        var il = constructor.Body.GetILProcessor();
        il.InsertBefore(start, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(start, il.Create(OpCodes.Newobj, listConstructor));
        il.InsertBefore(start, il.Create(OpCodes.Stfld, field));
    }

    void ReplaceGetter(PropertyDefinition prop, string columnName, MethodReference getValueReference)
    {
        /// A synthesized property getter looks like this:
        ///   0: ldarg.0
        ///   1: ldfld <backingField>
        ///   2: ret
        /// We want to change it so it looks like this:
        ///   0: ldarg.0
        ///   1: call Realms.RealmObject.get_IsManaged
        ///   2: brfalse.s 7
        ///   3: ldarg.0
        ///   4: ldstr <columnName>
        ///   5: call Realms.RealmObject.GetValue<T>
        ///   6: ret
        ///   7: ldarg.0
        ///   8: ldfld <backingField>
        ///   9: ret
        /// This is roughly equivalent to:
        ///   if (!base.IsManaged) return this.<backingField>;
        ///   else return base.GetValue<T>(<columnName>);

        var start = prop.GetMethod.Body.Instructions.First();
        var il = prop.GetMethod.Body.GetILProcessor();

        il.InsertBefore(start, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(start, il.Create(OpCodes.Call, realmObjectIsManagedGetter));
        il.InsertBefore(start, il.Create(OpCodes.Brfalse_S, start));
        il.InsertBefore(start, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(start, il.Create(OpCodes.Ldstr, columnName));
        il.InsertBefore(start, il.Create(OpCodes.Call, getValueReference));
        il.InsertBefore(start, il.Create(OpCodes.Ret));

        Debug.Write("[get] ");
    }

    void ReplaceSetter(PropertyDefinition prop, string columnName, MethodReference setValueReference)
    {
        /// A synthesized property setter looks like this:
        ///   0: ldarg.0
        ///   1: ldarg.1
        ///   2: stfld <backingField>
        ///   3: ret
        /// We want to change it so it looks like this:
        ///   0: ldarg.0
        ///   1: call Realm.RealmObject.get_IsManaged
        ///   2: brfalse.s 8
        ///   3: ldarg.0
        ///   4: ldstr <columnName>
        ///   5: ldarg.1
        ///   6: call Realm.RealmObject.SetValue<T>
        ///   7: ret
        ///   8: ldarg.0
        ///   9: ldarg.1
        ///   10: stfld <backingField>
        ///   11: ret
        /// This is roughly equivalent to:
        ///   if (!base.IsManaged) this.<backingField> = value;
        ///   else base.SetValue<T>(<columnName>, value);

        var start = prop.SetMethod.Body.Instructions.First();
        var il = prop.SetMethod.Body.GetILProcessor();

        il.InsertBefore(start, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(start, il.Create(OpCodes.Call, realmObjectIsManagedGetter));
        il.InsertBefore(start, il.Create(OpCodes.Brfalse_S, start));
        il.InsertBefore(start, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(start, il.Create(OpCodes.Ldstr, columnName));
        il.InsertBefore(start, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(start, il.Create(OpCodes.Call, setValueReference));
        il.InsertBefore(start, il.Create(OpCodes.Ret));

        Debug.Write("[set] ");
    }

    private static FieldReference GetBackingField(PropertyDefinition property)
    {
        return property.GetMethod.Body.Instructions
            .Where(o => o.OpCode == OpCodes.Ldfld)
            .Select(o => o.Operand)
            .OfType<FieldReference>()
            .SingleOrDefault();
    }
}