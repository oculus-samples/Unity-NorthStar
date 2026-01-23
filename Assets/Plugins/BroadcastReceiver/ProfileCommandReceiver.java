// Copyright (c) Meta Platforms, Inc. and affiliates.

package com.meta.northstar;

import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.BroadcastReceiver;

import android.content.Context;
import android.content.Intent;

import android.util.Log;
import android.os.Bundle;
import com.unity3d.player.UnityPlayer;

public class ProfileCommandReceiver extends BroadcastReceiver {
    public ProfileCommandReceiver() {
    }
 
    public static ProfileCommandInterface profileCommandInterface;

    @Override
    public void onReceive(Context context, Intent intent) {
        Log.d("NorthStar", "Received profiling command!");
        Log.d("NorthStar", "" + profileCommandInterface);
            
        Bundle bundle = intent.getExtras();
        if (bundle != null) {
            for (String key : bundle.keySet()) 
            {
                Log.e("NorthStar", key + " : " + (bundle.get(key) != null ? bundle.get(key) : "NULL"));

                Object value = bundle.get(key);
                if (value != null)
                {           
                    Log.e("NorthStar", ""+value.getClass());

                    if (value.getClass() == String.class)
                    {
                        if (profileCommandInterface != null) profileCommandInterface.setString(key, (String)value);
                    }
                    else if (value.getClass() == Float.class)
                    {
                        if (profileCommandInterface != null) profileCommandInterface.setFloat(key, (float)value);
                    }
                    else if (value.getClass() == Integer.class)
                    {
                        if (profileCommandInterface != null) profileCommandInterface.setInteger(key, (int)value);    
                    }
                    else if (value.getClass() == Boolean.class)
                    {
                        if (profileCommandInterface != null) profileCommandInterface.setBoolean(key, (boolean)value);    
                    }
                }                
            }
        }
    }    
}