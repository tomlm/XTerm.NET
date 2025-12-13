namespace XTerm.NET.Parser;

/// <summary>
/// Manages parameters for escape sequences.
/// </summary>
public class Params
{
    private readonly List<int> _params;
    private readonly List<int> _subParams;
    private int _subParamsStart;

    public int Length => _params.Count;

    public Params()
    {
        _params = new List<int>(32);
        _subParams = new List<int>(32);
        _subParamsStart = 0;
    }

    /// <summary>
    /// Gets a parameter at a specific index, or returns default value.
    /// </summary>
    public int GetParam(int index, int defaultValue = 0)
    {
        if (index >= 0 && index < _params.Count)
        {
            var value = _params[index];
            return value == -1 ? defaultValue : value;
        }
        return defaultValue;
    }

    /// <summary>
    /// Adds a parameter.
    /// </summary>
    public void AddParam(int value)
    {
        _params.Add(value);
    }

    /// <summary>
    /// Adds a sub-parameter.
    /// </summary>
    public void AddSubParam(int value)
    {
        _subParams.Add(value);
    }

    /// <summary>
    /// Gets sub-parameters for a specific parameter index.
    /// </summary>
    public List<int> GetSubParams(int index)
    {
        var result = new List<int>();
        if (index >= 0 && index < _params.Count)
        {
            // Sub-parameters are stored contiguously
            // This is a simplified version
            return result;
        }
        return result;
    }

    /// <summary>
    /// Resets the parameters.
    /// </summary>
    public void Reset()
    {
        _params.Clear();
        _subParams.Clear();
        _subParamsStart = 0;
    }

    /// <summary>
    /// Checks if a parameter exists at an index.
    /// </summary>
    public bool HasParam(int index)
    {
        return index >= 0 && index < _params.Count && _params[index] != -1;
    }

    /// <summary>
    /// Gets all parameters as an array.
    /// </summary>
    public int[] ToArray()
    {
        return _params.ToArray();
    }

    /// <summary>
    /// Clones the parameters.
    /// </summary>
    public Params Clone()
    {
        var clone = new Params();
        clone._params.AddRange(_params);
        clone._subParams.AddRange(_subParams);
        clone._subParamsStart = _subParamsStart;
        return clone;
    }
}
