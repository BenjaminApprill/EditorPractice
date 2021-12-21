using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PocketInjector
{
    public class Injector
    {
        public Dictionary<Type, IInjectable> aggregates;        

        public Injector()
        {
            BuildWithAssembly(Assembly.GetCallingAssembly());
        }

        public Injector(Assembly containingAssembly)
        {
            BuildWithAssembly(containingAssembly);
        }

        public T Return<T>()
        {
            try
            {
                return (T)aggregates[typeof(T)];
            }
            catch(KeyNotFoundException)
            {
                Type requestedType = typeof(T);

                if(requestedType.GetCustomAttribute(typeof(IBypassInjectionAttribute), false) != null)
                {
                    throw new PocketInjectorException(requestedType.ToString() + " is implementing the '[IBypassInjection]' attribute. It cannot be returned, as it was not injected.");
                }
                else if (requestedType.IsInterface == false && requestedType.IsAbstract)
                {
                    throw new PocketInjectorException(requestedType.Name + " is an abstract class. It cannot be injected, therefore it cannot be returned.");
                }
                else if (requestedType.IsInterface)
                {
                    throw new PocketInjectorException(requestedType.Name + " is an interface. It cannot be injected, therefore it cannot be returned.");
                }
                else
                {
                    throw new PocketInjectorException("Requested type has not been injected. " + requestedType.ToString() + " needs to inherit the IInjectable interface.");
                }
            }
        }

        Type[] GatherTypesParameterTypes(Type type)
        {
            ConstructorInfo[] constructorInfos = type.GetConstructors();

            if (constructorInfos.Length == 0)
            {
                return new Type[0];
            }
            else if (constructorInfos.Length == 1)
            {
                ParameterInfo[] parameterInfo = constructorInfos[0].GetParameters().ToArray();
                Type[] parameterTypes = new Type[parameterInfo.Length];

                for (int i = 0; i < parameterInfo.Length; i++)
                {
                    Type parameterType = parameterInfo[i].ParameterType;

                    if (parameterType == type)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject itself using the constructor. This creates an infinite injection loop, and overflows the stack... Self-injection cannot occur.");
                    }
                    else if (parameterType.GetCustomAttribute<IBypassInjectionAttribute>(false) != null)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + parameterType.ToString() + " through its constructor. This cannot occur because " + parameterType.ToString() + " is implementing the '[IBypassInjection]' attribute.");
                    }
                    else if (parameterType.IsAbstract && parameterType.IsInterface == false)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + parameterType.ToString() + " through its constructor. This cannot occur because " + parameterType.ToString() + " is an abstract class. Only implemented types can be injected.");
                    }
                    else if (parameterType.IsInterface)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + parameterType.ToString() + " through its constructor. This cannot occur because " + parameterType.ToString() + " is an interface. It has no injection instance.");
                    }
                    else if(typeof(IInjectable).IsAssignableFrom(parameterType) == false)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + parameterType.ToString() + " through its constructor. This cannot occur because " + parameterType.ToString() + " does not implement the 'IInjectable' interface.");
                    }                    
                    else if (typeof(IInjectable).IsAssignableFrom(parameterType))
                    {
                        parameterTypes[i] = parameterType;
                    }
                    else
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + parameterType.ToString() + ". This type is not injectable.");
                    }
                }
                return parameterTypes;
            }
            else
            {
                throw new PocketInjectorException("Injector cannot determine which constructor to use. " + type.ToString() + " must contain one constructor or no constructor.");
            }
        }

        object RecursiveInjection(Type type)
        {
            if(type.GetCustomAttribute<IBypassInjectionAttribute>(false) == null)
            {
                FieldInfo[] injectedFields = type.GetRuntimeFields()
                    .Where(field => field.GetCustomAttribute<InjectAttribute>() != null).ToArray();

                Type[] parameterTypes = GatherTypesParameterTypes(type);

                for (int i = 0; i < injectedFields.Length; i++)
                {
                    if (injectedFields[i].FieldType == type)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject itself with the '[Inject]' attribute. This creates an infinite injection loop, and overflows the stack... Self-injection cannot occur.");
                    }
                    else if (injectedFields[i].GetCustomAttribute<IBypassInjectionAttribute>(false) != null)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + injectedFields[i].FieldType.ToString() + " with the '[Inject]' attribute. This cannot occur because " + injectedFields[i].FieldType.ToString() + " is implementing the '[IBypassInjection]' attribute.");
                    }
                    else if(injectedFields[i].FieldType.IsInterface == false && injectedFields[i].FieldType.IsAbstract)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + injectedFields[i].FieldType.ToString() + " with the '[Inject]' attribute. This cannot occur because " + injectedFields[i].FieldType.ToString() + " is an abstract class-type.");
                    }
                    else if (injectedFields[i].FieldType.IsInterface)
                    {
                        throw new PocketInjectorException(type.ToString() + " is trying to inject " + injectedFields[i].FieldType.ToString() + " with the '[Inject]' attribute. This cannot occur because " + injectedFields[i].FieldType.ToString() + " is an interface.");
                    }
                }

                if (parameterTypes.Length == 0)
                {
                    object newInstance = Activator.CreateInstance(type);
                    aggregates.Add(type, (IInjectable)newInstance);

                    for (int i = 0; i < injectedFields.Length; i++)
                    {
                        if (aggregates.ContainsKey(injectedFields[i].FieldType))
                        {
                            injectedFields[i].SetValue(newInstance, aggregates[injectedFields[i].FieldType]);
                        }
                        else
                        {
                            if(injectedFields[i].FieldType.GetCustomAttribute<IBypassInjectionAttribute>(false) == null)
                            {
                                injectedFields[i].SetValue(newInstance, RecursiveInjection(injectedFields[i].FieldType));
                            }
                            else
                            {
                                throw new PocketInjectorException(type.ToString() + " is trying to '[Inject]' " + injectedFields[i].FieldType.ToString() + "... However, " + injectedFields[i].FieldType.ToString() + " is implementing the '[IBypassInection]' attribute, so it does not exist for injection.");
                            }
                        }
                    }

                    return newInstance;                    
                }
                else
                {
                    object[] dynamicConstructions = new object[parameterTypes.Length];

                    for (int i = 0; i < parameterTypes.Length; i++)
                    {
                        if (aggregates.ContainsKey(parameterTypes[i]))
                        {
                            dynamicConstructions[i] = aggregates[parameterTypes[i]];
                        }
                        else
                        {
                            dynamicConstructions[i] = RecursiveInjection(parameterTypes[i]);
                        }
                    }

                    object newInstance;

                    if(aggregates.ContainsKey(type) == true)
                    {
                        newInstance = aggregates[type];
                    }
                    else
                    {
                        newInstance = Activator.CreateInstance(type, dynamicConstructions);
                        aggregates.Add(type, (IInjectable)newInstance);

                        for (int i = 0; i < injectedFields.Length; i++)
                        {
                            if (aggregates.ContainsKey(injectedFields[i].FieldType))
                            {
                                injectedFields[i].SetValue(newInstance, aggregates[injectedFields[i].FieldType]);
                            }
                            else
                            {
                                injectedFields[i].SetValue(newInstance, RecursiveInjection(injectedFields[i].FieldType));
                            }
                        }
                    }

                    return newInstance;
                }
            }
            else
            {                
                if(type.GetRuntimeFields().Where(field => field.GetCustomAttribute<InjectAttribute>() != null).ToArray().Length > 0)
                {
                    throw new PocketInjectorException(type.ToString() + " implements the '[IBypassInjection]' attribute, but has a field trying to implement the '[Inject]' attribute. Classes bypassed for injection cannot have their fields injected.");
                }
                else
                {
                    throw new PocketInjectorException(type.ToString() + " implements the '[IBypassInjection]' attribute. It is not being injected.");
                }
            }
        }

        void BuildWithAssembly(Assembly containingAssembly)
        {
            aggregates = new Dictionary<Type, IInjectable>();

            var typesQuery = containingAssembly.GetTypes()
                .Where(t => typeof(IInjectable).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Where(t => t.GetCustomAttribute(typeof(IBypassInjectionAttribute), false) == null);

            Type[] types = typesQuery.ToArray();

            for (int i = 0; i < types.Length; i++)
            {
                if (aggregates.ContainsKey(types[i]) == false)
                {
                    RecursiveInjection(types[i]);
                }
            }
        }
    }

    public interface IInjectable { };

    [AttributeUsage(AttributeTargets.Class)]
    public class IBypassInjectionAttribute : Attribute{ };

    [AttributeUsage(AttributeTargets.Field)]
    public class InjectAttribute : Attribute { };

    public class PocketInjectorException : Exception
    {
        public PocketInjectorException()
        {
        }

        public PocketInjectorException(string message)
            : base(message)
        {
        }

        public PocketInjectorException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }    
}