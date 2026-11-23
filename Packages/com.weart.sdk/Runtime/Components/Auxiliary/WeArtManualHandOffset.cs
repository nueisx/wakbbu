using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using WeArt.Components;

public class WeArtManualHandOffset : MonoBehaviour
{
    public WeArtDeviceTrackingObject trackingObject;
    public InputField PosX;
    public InputField PosY;
    public InputField PosZ;
    public InputField RotX;
    public InputField RotY;
    public InputField RotZ;

    public enum TransformValues
    {
        PosX, PosY, PosZ, RotX, RotY, RotZ
    }

    private float _posX;
    private float _posY;
    private float _posZ;
    private float _rotX;
    private float _rotY;
    private float _rotZ;


    private bool _isPosXPlusPressed;
    private bool _isPosYPlusPressed;
    private bool _isPosZPlusPressed;
    private bool _isRotXPlusPressed;
    private bool _isRotYPlusPressed;
    private bool _isRotZPlusPressed;

    private bool _isPosXMinusPressed;
    private bool _isPosYMinusPressed;
    private bool _isPosZMinusPressed;
    private bool _isRotXMinusPressed;
    private bool _isRotYMinusPressed;
    private bool _isRotZMinusPressed;

    private float _posXPlusTimePressed;
    private float _posYPlusTimePressed;
    private float _posZPlusTimePressed;
    private float _rotXPlusTimePressed;
    private float _rotYPlusTimePressed;
    private float _rotZPlusTimePressed;

    private float _posXMinusTimePressed;
    private float _posYMinusTimePressed;
    private float _posZMinusTimePressed;
    private float _rotXMinusTimePressed;
    private float _rotYMinusTimePressed;
    private float _rotZMinusTimePressed;

    float timePressedTool = 0;

    void Start()
    {
        PosX.text = trackingObject.PositionOffset.x.ToString();
        PosY.text = trackingObject.PositionOffset.y.ToString();
        PosZ.text = trackingObject.PositionOffset.z.ToString();

        RotX.text = trackingObject.RotationOffset.x.ToString();
        RotY.text = trackingObject.RotationOffset.y.ToString();
        RotZ.text = trackingObject.RotationOffset.z.ToString();

        _posX = float.Parse(trackingObject.PositionOffset.x.ToString());
        _posY = float.Parse(trackingObject.PositionOffset.y.ToString());
        _posZ = float.Parse(trackingObject.PositionOffset.z.ToString());

        _rotX = float.Parse(trackingObject.RotationOffset.x.ToString());
        _rotY = float.Parse(trackingObject.RotationOffset.y.ToString());
        _rotZ = float.Parse(trackingObject.RotationOffset.z.ToString());
    }

    // Update is called once per frame
    void Update()
    {
        AddValue(TransformValues.PosX, _posX, _isPosXPlusPressed, _posXPlusTimePressed);
        AddValue(TransformValues.PosY, _posY, _isPosYPlusPressed, _posYPlusTimePressed);
        AddValue(TransformValues.PosZ, _posZ, _isPosZPlusPressed, _posZPlusTimePressed);

        AddValue(TransformValues.RotX, _rotX, _isRotXPlusPressed, _rotXPlusTimePressed);
        AddValue(TransformValues.RotY, _rotY, _isRotYPlusPressed, _rotYPlusTimePressed);
        AddValue(TransformValues.RotZ, _rotZ, _isRotZPlusPressed, _rotZPlusTimePressed);

        RemoveValue(TransformValues.PosX, _posX, _isPosXMinusPressed, _posXMinusTimePressed);
        RemoveValue(TransformValues.PosY, _posY, _isPosYMinusPressed, _posYMinusTimePressed);
        RemoveValue(TransformValues.PosZ, _posZ, _isPosZMinusPressed, _posZMinusTimePressed);
        RemoveValue(TransformValues.RotX, _rotX, _isRotXMinusPressed, _rotXMinusTimePressed);
        RemoveValue(TransformValues.RotY, _rotY, _isRotYMinusPressed, _rotYMinusTimePressed);
        RemoveValue(TransformValues.RotZ, _rotZ, _isRotZMinusPressed, _rotZMinusTimePressed);

        trackingObject.PositionOffset = new Vector3(_posX, _posY, _posZ);
        trackingObject.RotationOffset = new Vector3(_rotX, _rotY, _rotZ);

        PosX.text = trackingObject.PositionOffset.x.ToString();
        PosY.text = trackingObject.PositionOffset.y.ToString();
        PosZ.text = trackingObject.PositionOffset.z.ToString();

        RotX.text = trackingObject.RotationOffset.x.ToString();
        RotY.text = trackingObject.RotationOffset.y.ToString();
        RotZ.text = trackingObject.RotationOffset.z.ToString();
    }

    private void AddValue(TransformValues plusValues, float value, bool isPressed, float timePressed)
    {
        if (!isPressed)
            return;

        bool isRotation = false;
         if( plusValues == TransformValues.RotX || plusValues == TransformValues.RotY || plusValues == TransformValues.RotZ)
            isRotation = true;


        timePressedTool = timePressed + Time.deltaTime;

        switch (plusValues) {
            case TransformValues.PosX:
                _posXPlusTimePressed = timePressedTool;
                break;
            case TransformValues.PosY:
                _posYPlusTimePressed = timePressedTool;
                break;
            case TransformValues.PosZ:
                _posZPlusTimePressed = timePressedTool;
                break;
            case TransformValues.RotX:
                _rotXPlusTimePressed = timePressedTool;
                break;
            case TransformValues.RotY:
                _rotYPlusTimePressed = timePressedTool;
                break;
            case TransformValues.RotZ:
                _rotZPlusTimePressed = timePressedTool;
                break;
        }

        if (timePressed < 1)
            return;

        float timePressedInSteps = Mathf.Floor(timePressed / 0.5f);

        if (timePressedInSteps != Mathf.Floor(timePressedTool / 0.5f))
        {
            if (timePressedInSteps > 20)
            {
                if (isRotation)
                    value += 10f;
                else
                value += 1f;

            }
            else
            {
                if (timePressedInSteps > 10)
                {
                    if (isRotation)
                        value += 1f;
                    else
                        value += 0.1f;

                }
                else
                {
                    if (isRotation)
                        value += 0.1f;
                    else
                        value += 0.01f;
                }
            }
        }

        if(value>360)
            value = 0f;

        switch (plusValues)
        {
            case TransformValues.PosX:
                _posX = value;
                break;
            case TransformValues.PosY:
                _posY = value;
                break;
            case TransformValues.PosZ:
                _posZ = value;
                break;

            case TransformValues.RotX:
                _rotX = value;
                break;
            case TransformValues.RotY:
                _rotY = value;
                break;
            case TransformValues.RotZ:
                _rotZ = value;
                break;
        }


    }

    private void RemoveValue(TransformValues minusValues, float value, bool isPressed, float timePressed)
    {
        if (!isPressed)
            return;

        bool isRotation = false;
        if (minusValues == TransformValues.RotX || minusValues == TransformValues.RotY || minusValues == TransformValues.RotZ)
            isRotation = true;

        timePressedTool = timePressed + Time.deltaTime;

        switch (minusValues)
        {
            case TransformValues.PosX:
                _posXMinusTimePressed = timePressedTool;
                break;
            case TransformValues.PosY:
                _posYMinusTimePressed = timePressedTool;
                break;
            case TransformValues.PosZ:
                _posZMinusTimePressed = timePressedTool;
                break;
            case TransformValues.RotX:
                _rotXMinusTimePressed = timePressedTool;
                break;
            case TransformValues.RotY:
                _rotYMinusTimePressed = timePressedTool;
                break;
            case TransformValues.RotZ:
                _rotZMinusTimePressed = timePressedTool;
                break;
        }

        if (timePressed < 1)
            return;

        float timePressedInSteps = Mathf.Floor(timePressed / 0.5f);

        if (timePressedInSteps != Mathf.Floor(timePressedTool / 0.5f))
        {
            if (timePressedInSteps > 20)
            {
                if (isRotation)
                    value -= 10f;
                else
                    value -= 1f;

            }
            else
            {
                if (timePressedInSteps > 10)
                {
                    if (isRotation)
                        value -= 1f;
                    else
                        value -= 0.1f;

                }
                else
                {
                    if (isRotation)
                        value -= 0.1f;
                    else
                        value -= 0.01f;
                }
            }
        }

        switch (minusValues)
        {
            case TransformValues.PosX:
                _posX = value;
                break;
            case TransformValues.PosY:
                _posY = value;
                break;
            case TransformValues.PosZ:
                _posZ = value;
                break;

            case TransformValues.RotX:
                _rotX = value;
                break;
            case TransformValues.RotY:
                _rotY = value;
                break;
            case TransformValues.RotZ:
                _rotZ = value;
                break;
        }


    }
    public void PosXPlusEnter()
    {
        _isPosXPlusPressed = true;
        _posX += 0.01f;
    }

    public void PosXPlusExit()
    {
        _isPosXPlusPressed = false;
        _posXPlusTimePressed = 0;
    }

    public void PosYPlusEnter()
    {
        _isPosYPlusPressed = true;
        _posY += 0.01f;

    }

    public void PosYPlusExit()
    {
        _isPosYPlusPressed = false;
        _posYPlusTimePressed = 0;
    }

    public void PosZPlusEnter()
    {
        _isPosZPlusPressed = true;
        _posZ += 0.01f;
    }

    public void PosZPlusExit()
    {
        _isPosZPlusPressed = false;
        _posZPlusTimePressed = 0;
    }



    public void RotXPlusEnter()
    {
        _isRotXPlusPressed = true;
        _rotX += 0.1f;
    }

    public void RotXPlusExit()
    {
        _isRotXPlusPressed = false;
        _rotXPlusTimePressed = 0;
    }

    public void RotYPlusEnter()
    {
        _isRotYPlusPressed = true;
        _rotY += 0.1f;
    }

    public void RotYPlusExit()
    {
        _isRotYPlusPressed = false;
        _rotYPlusTimePressed = 0;
    }

    public void RotZPlusEnter()
    {
        _isRotZPlusPressed = true;
        _rotZ += 0.1f;
    }

    public void RotZPlusExit()
    {
        _isRotZPlusPressed = false;
        _rotZPlusTimePressed = 0;
    }


    // Minus


    public void PosXMinusEnter()
    {
        _isPosXMinusPressed = true;
        _posX -= 0.01f;
    }

    public void PosXMinusExit()
    {
        _isPosXMinusPressed = false;
        _posXMinusTimePressed = 0;
    }

    public void PosYMinusEnter()
    {
        _isPosYMinusPressed = true;
        _posY -= 0.01f;

    }

    public void PosYMinusExit()
    {
        _isPosYMinusPressed = false;
        _posYMinusTimePressed = 0;
    }

    public void PosZMinusEnter()
    {
        _isPosZMinusPressed = true;
        _posZ -= 0.01f;
    }

    public void PosZMinusExit()
    {
        _isPosZMinusPressed = false;
        _posZMinusTimePressed = 0;
    }



    public void RotXMinusEnter()
    {
        _isRotXMinusPressed = true;
        _rotX -= 0.1f;
    }

    public void RotXMinusExit()
    {
        _isRotXMinusPressed = false;
        _rotXMinusTimePressed = 0;
    }

    public void RotYMinusEnter()
    {
        _isRotYMinusPressed = true;
        _rotY -= 0.1f;
    }

    public void RotYMinusExit()
    {
        _isRotYMinusPressed = false;
        _rotYMinusTimePressed = 0;
    }

    public void RotZMinusEnter()
    {
        _isRotZMinusPressed = true;
        _rotZ -= 0.1f;
    }

    public void RotZMinusExit()
    {
        _isRotZMinusPressed = false;
        _rotZMinusTimePressed = 0;
    }
}
