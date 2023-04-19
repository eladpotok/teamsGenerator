import React from "react";
import { AnalyticsEventManager } from "../Adapters/AnalyticsEventSender";

export const AnalyticsContext = React.createContext({
    sendContentEvent: (contentType, page_title) => {},
    sendPageViewEvent: (page_title) => {}
});

export const AnalyticsContextProvider = (props) => {
    
    const analyticsEventManager = new AnalyticsEventManager()

    function sendContentEvent(contentType, page_title) {
        analyticsEventManager.sendContentEvent(contentType, page_title)
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

