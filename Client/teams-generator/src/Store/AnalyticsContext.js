import React from "react";
import { AnalyticsEventManager } from "../Adapters/AnalyticsEventSender";

export const AnalyticsContext = React.createContext({
    sendContentEvent: (contentType, content_id) => {},
    sendPageViewEvent: (page_title) => {}
});

export const AnalyticsContextProvider = (props) => {
    
    const analyticsEventManager = new AnalyticsEventManager()

    function sendContentEvent(contentType, content_id) {
        analyticsEventManager.sendContentEvent(contentType, content_id)
    }

    function sendPageViewEvent(page_title){
        analyticsEventManager.sendPageViewEvent(page_title)
    }

    return <AnalyticsContext.Provider value={{
        sendContentEvent,
        sendPageViewEvent
    }}>
        {props.children}    
    </AnalyticsContext.Provider>
};

