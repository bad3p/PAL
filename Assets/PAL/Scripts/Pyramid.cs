//
// POLYGONAL AREA LIGHTS
// The MIT License (MIT)
// Copyright (c) 2016-2017 ALEXANDER PETRYAEV
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is furnished 
// to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A 
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION 
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using UnityEngine;
using System.Collections;

public class Pyramid : MonoBehaviour 
{
	public Transform EdgeHandler0;
	public Transform EdgeHandler1;
	public Transform EdgeHandler2;

	void Update () 
	{
	}

	void OnDrawGizmos() 
	{
		Gizmos.color = Color.black;
		Gizmos.DrawSphere( Vector3.zero, 1 );

		Gizmos.color = Color.red;
		Gizmos.DrawLine( Vector3.zero, EdgeHandler0.transform.position );

		Gizmos.color = Color.green;
		Gizmos.DrawLine( Vector3.zero, EdgeHandler1.transform.position );

		Gizmos.color = Color.blue;
		Gizmos.DrawLine( Vector3.zero, EdgeHandler2.transform.position );

		Vector3 edge0 = EdgeHandler0.transform.position.normalized;
		Vector3 edge1 = EdgeHandler1.transform.position.normalized;
		Vector3 edge2 = EdgeHandler2.transform.position.normalized;

		float alpha = EdgeHandler0.transform.position.magnitude;
		float beta = alpha;
		float gamma = alpha;

		Gizmos.color = Color.yellow;
		Gizmos.DrawLine( edge0 * alpha, edge1 * beta );
		Gizmos.DrawLine( edge1 * beta, edge2 * gamma );
		Gizmos.DrawLine( edge2 * gamma, edge0 * alpha );
		Gizmos.DrawLine( Vector3.zero, ( edge0 * alpha + edge1 * beta + edge2 * gamma)/3 );
	}
}
