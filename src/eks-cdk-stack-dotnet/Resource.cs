using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CDK;

#nullable enable

public static class Resource 
{
  #region "cdk.json" file (context) parameters (and "-c" command line args)
  // Context info: https://docs.aws.amazon.com/cdk/latest/guide/context.html

  public static TRes? Create<TRes>(this Construct scope, string name, object? props)
      where TRes : Construct 
    =>
      (TRes?)Activator.CreateInstance(typeof(TRes), scope, name, props);

  public static Nullable<T> GetCtxValue<T>(this Construct scope, string name) where T : struct
  {
    T defaultValue = default(T);
    T retVal = scope.GetCtxValue(name, defaultValue);
    
    if(retVal.Equals(defaultValue))
      return null;
    return retVal;
  }

  public static T GetCtxValue<T>(this Construct scope, string name, T defaultValue) where T : struct
  {
    object retVal = scope.Node.TryGetContext(name);
    if(retVal == null)
      return defaultValue;
    return (T)retVal;
  }

  public static IEnumerable<string>? GetCtxStrings(this Construct scope, string name, params char[] delimiters)
  {
    string? delimited = scope.GetCtxString(name);
    if(delimited == null)
      return null;
    return delimited.StringToCollection(delimiters);
  }

  public static IEnumerable<string> GetCtxStrings(this Construct scope, string name, string defaultValue, params char[] delimiters)
    => scope.GetCtxString(name, defaultValue).StringToCollection(delimiters);

  private static IEnumerable<string> StringToCollection(this string delimited, params char[] delimiters)
  {
    if(delimiters.Count() == 0)
      delimiters = new char[] {',', ';'};
    return delimited.Split(delimiters).Select(s => s.Trim());
  }

  public static string GetCtxString(this Construct scope, string name, string defaultValue)
  { 
    string retVal = scope.GetCtx<string>(name, defaultValue);
    return string.IsNullOrEmpty(retVal) ? defaultValue : retVal;
  }

  public static string? GetCtxString(this Construct scope, string name)
  {
    string? retVal = scope.Node.TryGetContext(name) as string;
    return string.IsNullOrEmpty(retVal) ? (string?)null : retVal;
  }

  public static T? GetCtx<T>(this Construct scope, string name) where T : class, new()
  {
    T defaultValue = new T();
    T retVal = scope.GetCtx(name, defaultValue);
    
    if(retVal == defaultValue)
      return null;
    return retVal;
  }

  public static T GetCtx<T>(this Construct scope, string name, T defaultValue) where T : class
  { 
    object retVal = scope.Node.TryGetContext(name);
    if(retVal == null)
      return defaultValue;
    return (T)retVal;
  }
  #endregion "cdk.json" file (context) parameters (and "-c" command line args)

  #region CfnParameter helpers
  public static CfnParameter Param(this Construct scope, string paramName, string defaultValue, string description, string type = "String")
    =>
      new CfnParameter(scope, paramName, new CfnParameterProps{
        Default = defaultValue,
        Description = description,
        Type = string.IsNullOrWhiteSpace(type) ? "String" : type
      });

  public static CfnParameter ListParam(this Construct scope, string paramName, string defaultValue, string description)
    => scope.Param(paramName, defaultValue, description, "CommaDelimitedList");

  public static CfnParameter Param(this Construct scope, string paramName, double defaultValue, string description)
    =>
      new CfnParameter(scope, paramName, new CfnParameterProps{
        Default = defaultValue,
        Description = description,
        Type="Number"
      });
  #endregion CfnParameter helpers
}