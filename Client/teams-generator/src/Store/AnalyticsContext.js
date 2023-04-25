import React from "react";
import { AnalyticsEventManager } from "../Adapters/AnalyticsEventSender";
import { sendEvent } from "../Adapters/StorageAdapter";


export const AnalyticsContext = React.createContext({
    sendAnalyticsEngagement: (user, type, data) => {},
    sendAnalyticsImpression: (user, type) => {}
});

export const AnalyticsContextProvider = (props) => {

    function sendAnalyticsEngagement(user, type, data) {
        sendEvent('engagement', user, type, data)
    }

    function sendAnalyticsImpression(user, type) {
        sendEvent('impression', user, type, null)
    }

    return <AnalyticsContext.Provider value={{
        sendAnalyticsEngagement,
        sendAnalyticsImpression
    }}>
        {props.children}    
    </AnalyticsContext.Provider>
};

