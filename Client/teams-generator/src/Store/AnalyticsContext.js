import React from "react";
import { AnalyticsEventManager } from "../Adapters/AnalyticsEventSender";
import { sendEvent } from "../Adapters/StorageAdapter";


export const AnalyticsContext = React.createContext({
    sendContentEvent: (contentType, content_id) => {},
    sendPageViewEvent: (page_title) => {},
    sendAnalyticsEngagement: (user, type, data) => {},
    sendAnalyticsImpression: (user, type) => {}
});

export const AnalyticsContextProvider = (props) => {
    
    const analyticsEventManager = new AnalyticsEventManager()

    function sendContentEvent(contentType, content_id) {
        analyticsEventManager.sendContentEvent(contentType, content_id)
        
    }

    function sendAnalyticsEngagement(user, type, data) {
        sendEvent('engagement', user, type, data)
    }

    function sendAnalyticsImpression(user, type) {
        sendEvent('impression', user, type, null)
    }


    function sendPageViewEvent(page_title){
        analyticsEventManager.sendPageViewEvent(page_title)
    }

    return <AnalyticsContext.Provider value={{
        sendContentEvent,
        sendPageViewEvent,
        sendAnalyticsEngagement,
        sendAnalyticsImpression
    }}>
        {props.children}    
    </AnalyticsContext.Provider>
};

