namespace BoundsExtensions;

using System;
using System.Collections.Generic;

using UnityEngine;

// Axis-aligned rectangle in 3d-space
struct RectFace {
	private int zeroIdx;
	private Bounds _bounds;
	private bool negative=false;
	
	public Bounds bounds {get {return _bounds;}}
	
	public Vector3 perpindicular {
		get {
			return new Vector3(
				zeroIdx==0 ? (negative ? -1:1):0, 
				zeroIdx==1 ? (negative ? -1:1):0, 
				zeroIdx==2 ? (negative ? -1:1):0
			);
		}
	}
	
	public RectFace(Vector3 min, Vector3 max, bool negative=false) {
		Vector3 size = max - min;
		bool forElse = true;
		for (int i=0; i<3; i++) {
			if (size[i] == 0) {
				forElse = false;
				this.zeroIdx = i;
				break;
			}
		} if (forElse) {
			throw new ArgumentException("RectFace must have zero width in at least one dimension");
		}
		
		this._bounds = new Bounds(0.5f*(min+max),max-min);
		this._bounds.FixExtents();
		this.negative = negative;
	}
	
	public static bool IsAcceptableParams(Vector3 min, Vector3 max) {
		Vector3 v = max - min;
		for (int i=0; i<3; i++) {
			if (v[i] == 0) return true;
		}
		return false;
	}
}

static class BoundsExtension {
	public static void FixExtents(this ref Bounds ths) {
		Vector3 ext = ths.extents;
		for (int i=0; i<3; i++) {
			ext[i] = Math.Abs(ext[i]);
		}
		ths.extents = ext;
	}
	
	// Always returns 6 faces... because its a rect prism. Wow. 
	// Faces' perpindicular property all point *outside* the bounding box
	public static RectFace[] GetFaces(this Bounds ths) {
		RectFace[] rt = new RectFace[6];
		Vector3 a = ths.center + ths.extents;
		rt[0] = new RectFace( //+x
			a, ths.center + Vector3.Scale(ths.extents,new Vector3( 1,-1,-1))
		);
		rt[1] = new RectFace( //+y
			a, ths.center + Vector3.Scale(ths.extents,new Vector3(-1, 1,-1))
		);
		rt[2] = new RectFace( //+z
			a, ths.center + Vector3.Scale(ths.extents,new Vector3(-1,-1, 1))
		);
		a = ths.center - ths.extents;
		rt[3] = new RectFace( //-x
			a, ths.center + Vector3.Scale(ths.extents,new Vector3(-1, 1, 1)), true
		);
		rt[4] = new RectFace( //-y
			a, ths.center + Vector3.Scale(ths.extents,new Vector3( 1,-1, 1)), true
		);
		rt[5] = new RectFace( //-z
			a, ths.center + Vector3.Scale(ths.extents,new Vector3( 1, 1,-1)), true
		);
		
		return rt;
	}
	
	public static RectFace ClosestFace(this Bounds ths,Vector3 point) {
		RectFace[] faces = ths.GetFaces();
		
		RectFace closest = faces[0];
		float closest_dist = closest.bounds.SqrDistance(point);
		for (int i=1; i<6; i++) {
			RectFace f = faces[i];
			float dist = f.bounds.SqrDistance(point);
			if (dist < closest_dist) {
				closest = f;
				closest_dist = dist;
				if (dist == 0) return closest;
			}
		}
		return closest;
	}
	
	public static Quaternion AwayRotation(this Bounds ths, Vector3 point) {
		RectFace closest = ths.ClosestFace(point);
		Vector3 dir = closest.perpindicular;
		
		return Quaternion.LookRotation(dir);
	}
}