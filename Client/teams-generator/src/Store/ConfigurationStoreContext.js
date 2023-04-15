import React, { useState } from "react";

export const ConfigurationStoreContext = React.createContext({
    storeConfig: [],
    setStoreConfig: (config) => {}

});

export const ConfigurationStoreContextProvider = (props) => {

    const [storeConfig, setStoreConfig] = useState(null)

    return <ConfigurationStoreContext.Provider value={{
        storeConfig,
        setStoreConfig
    }}>
        {props.children}    
    </ConfigurationStoreContext.Provider>
};

