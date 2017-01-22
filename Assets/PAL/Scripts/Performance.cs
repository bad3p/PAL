using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Performance : MonoBehaviour 
{
	public int NumTestFrames = 100;
	public int NumWarmupFrames = 10;

	private MeshAreaLight[] _meshAreaLights;

	private int _numAreaLights = 0;
	private int _numTestFramesRemain = 0;
	private int _numWarmupFramesRemain = 0;
	private float _testStartTime = 0;

	private Dictionary<int,float> _results = new Dictionary<int,float>();

	void Start() 
	{
		_meshAreaLights = GetComponentsInChildren<MeshAreaLight>( true );

		foreach( var meshAreaLight in _meshAreaLights )
		{
			meshAreaLight.gameObject.SetActive( false );
		}

		_numAreaLights = 0;
		_numTestFramesRemain = NumTestFrames;
		_numWarmupFramesRemain = NumWarmupFrames;
		_testStartTime = 0;
	}
	
	void Update () 
	{
		if( _numWarmupFramesRemain > 0 )
		{
			_numWarmupFramesRemain--;
			return;
		}

		if( _numTestFramesRemain > 0 )
		{
			if( _numTestFramesRemain == NumTestFrames )
			{
				_testStartTime = Time.realtimeSinceStartup;
			}

			_numTestFramesRemain--;
			return;
		}
		else if( _numAreaLights < _meshAreaLights.Length )
		{
			float testTime = Time.realtimeSinceStartup - _testStartTime;
			float fps = NumTestFrames / testTime;
			Debug.Log( "_numAreaLights = " + _numAreaLights.ToString() + " time = " + testTime.ToString("F2") + " fps = " + fps.ToString("F2") );
			if( _results != null )
			{
				if( !_results.ContainsKey( _numAreaLights ) )
				{
					_results.Add( _numAreaLights, fps );
				}
			}

			_meshAreaLights[_numAreaLights].gameObject.SetActive( true );
			_numAreaLights++;
			_numTestFramesRemain = NumTestFrames;
			_numWarmupFramesRemain = NumWarmupFrames;
			_testStartTime = 0;

			if( _numAreaLights == _meshAreaLights.Length && _results != null )
			{
				System.IO.StreamWriter streamWriter = new System.IO.StreamWriter( "PerformanceReport.txt", false, System.Text.Encoding.ASCII );
				foreach( var key in _results.Keys )
				{
					string report = key.ToString() + ", " + _results[key].ToString("F2") + "\n";
					streamWriter.Write( report );
				}
				streamWriter.Close();
				_results = null;
			}
		}
	}
}
